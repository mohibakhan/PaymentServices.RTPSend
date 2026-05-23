using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.RTPSend.Exceptions.Core;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Interface;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Interface.Services;
using PaymentServices.RTPSend.Models;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;
using PaymentServices.RTPSend.Models.Response;
using PaymentServices.RTPSend.Settings;

namespace PaymentServices.RTPSend.Functions;

/// <summary>
/// HTTP entry point — strictly synchronous validation + persistence.
/// The full business pipeline (PartnerLedger → Limit → Ledger → TabaPay)
/// runs in <see cref="ProcessPayment"/>, triggered off the SB queue.
///
/// Target HTTP latency: 50–150 ms.
/// </summary>
public class CreatePayment
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IPaymentCosmosDBAdapter _paymentCosmosDBService;
    private readonly IPaymentIdempotencyAdapter _idempotency;
    private readonly IEvolvePaymentRequestHelper _evolvePaymentRequestHelper;
    private readonly IApiUserConfigCosmosAdapter _apiConfig;
    private readonly IValidator<BasicPaymentRequest> _validator;
    private readonly IProblemHelper _problemHelper;
    private readonly IServiceBusMessageService _serviceBus;
    private readonly RtpSendSettings _settings;
    private readonly ILogger<CreatePayment> _log;

    public CreatePayment(
        IPaymentCosmosDBAdapter paymentCosmosDBService,
        IPaymentIdempotencyAdapter idempotency,
        IEvolvePaymentRequestHelper evolvePaymentRequestHelper,
        IApiUserConfigCosmosAdapter apiConfig,
        IValidator<BasicPaymentRequest> validator,
        IProblemHelper problemHelper,
        IServiceBusMessageService serviceBus,
        IOptions<RtpSendSettings> settings,
        ILogger<CreatePayment> logger)
    {
        _paymentCosmosDBService = paymentCosmosDBService;
        _idempotency = idempotency;
        _evolvePaymentRequestHelper = evolvePaymentRequestHelper;
        _apiConfig = apiConfig;
        _validator = validator;
        _problemHelper = problemHelper;
        _serviceBus = serviceBus;
        _settings = settings.Value;
        _log = logger;
    }

    [Function("CreatePayment")]
    [OpenApiOperation(operationId: "CreatePayment", tags: new[] { "CreatePayment" })]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(BasicPaymentRequest), Description = "BasicPaymentRequest", Required = true)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Accepted, contentType: "application/json", bodyType: typeof(CreatePaymentResponse), Description = "Accepted")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(Problem), Description = "Bad Request")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Conflict, contentType: "application/json", bodyType: typeof(Problem), Description = "Conflict")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "application/json", bodyType: typeof(Problem), Description = "Internal Server Error")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req)
    {
        try
        {
            // -------- Parse request body --------
            var userRequest = await JsonSerializer.DeserializeAsync<BasicPaymentRequest>(req.Body, _jsonOptions);
            _log.LogInformation("Request body: {Body}", JsonSerializer.Serialize(userRequest));

            // -------- Header & body presence checks --------
            var headers = req.Headers;
            var clientId = headers["x-client-id"].ToString();
            var merchantId = headers["x-merchant-id"].ToString();
            var subscriptionKey = headers["ocp-apim-subscription-key"].ToString();

            if (userRequest is null ||
                string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(merchantId) ||
                string.IsNullOrWhiteSpace(subscriptionKey))
            {
                return BuildProblemResponse(req.HttpContext, new ValidationProblem());
            }

            // -------- FluentValidation --------
            var fluentResult = await _validator.ValidateAsync(userRequest);
            if (!fluentResult.IsValid)
            {
                return BuildProblemResponse(req.HttpContext, new ValidationProblem
                {
                    InvalidParams = fluentResult.Errors.Select(e => new InvalidParams
                    {
                        Details = e.PropertyName,
                        Message = e.ErrorMessage
                    }).ToList()
                });
            }

            // -------- API user config (auth check) --------
            var apiUserConfig = await _apiConfig.GetApiUserConfigAsync(clientId, merchantId, subscriptionKey);
            if (apiUserConfig is null)
                return BuildProblemResponse(req.HttpContext, new ForbiddenProblem());

            // -------- Build the EvolvePaymentRequest doc --------
            var payment = _evolvePaymentRequestHelper.ConvertBasicToEvolveRequest(
                userRequest, headers, apiUserConfig.PmtHandler);

            // -------- Atomic dedupe via idempotency container --------
            // Reserve the paymentReference FIRST. If it's already taken we
            // fail fast with 409 and never touch the main paymentRequests
            // container. Worst case (idempotency succeeds but paymentRequests
            // insert fails below): the idempotency entry auto-expires via TTL
            // and the client can retry with a new paymentReference.
            var reserved = await _idempotency.TryReserveAsync(new PaymentIdempotencyEntry
            {
                Id = userRequest.PaymentReference,
                PaymentReference = userRequest.PaymentReference,
                EvolveId = payment.EvolveId,
                CreatedAt = DateTime.UtcNow.ToCosmosDateTime()
            });

            if (!reserved)
                return BuildProblemResponse(req.HttpContext, new ConflictProblem());

            // -------- Persist the main payment document --------
            var created = await _paymentCosmosDBService.CreateItemAsync(payment);
            if (created is null)
            {
                // Extremely unlikely — idempotency reserved this paymentReference
                // and the doc id is a fresh Guid (evolveId). If it does happen,
                // surface as Conflict so the client can retry.
                _log.LogError(
                    "Idempotency reserved {Ref} but paymentRequests insert returned conflict. " +
                    "evolveId {EvolveId} may collide.",
                    userRequest.PaymentReference, payment.EvolveId);
                return BuildProblemResponse(req.HttpContext, new ConflictProblem());
            }

            // -------- Publish to processing subscription via shared topic --------
            await _serviceBus.SendToQueueAsync(
                new PaymentQueueMessage
                {
                    EvolveId = payment.EvolveId,
                    PaymentReference = payment.PaymentReference,
                    EnqueuedAt = DateTimeOffset.UtcNow
                },
                PaymentRequestConstants.ServiceBusTopicName,
                subject: PaymentRequestConstants.CreatePaymentRequestSubject);

            // -------- Return 202 Accepted --------
            var response = new CreatePaymentResponse
            {
                EvolveId = payment.EvolveId,
                PaymentReference = payment.PaymentReference,
                Status = "Accepted"
            };
            _log.LogInformation("Response: {Body}", JsonSerializer.Serialize(response));
            return new AcceptedResult(string.Empty, response);
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "JsonException: {Message}", ex.Message);
            return BuildProblemResponse(req.HttpContext, new ValidationProblem
            {
                Detail = "Malformed JSON body."
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled error: {Message}", ex.Message);
            return BuildProblemResponse(req.HttpContext, new UnexpectedProblem());
        }
    }

    private IActionResult BuildProblemResponse(HttpContext context, Problem problem)
    {
        var traceId = _problemHelper.GetTraceId(context);
        problem.TraceId = traceId;
        problem.ReferenceCode = _problemHelper.GenerateReferenceCode(traceId);
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
