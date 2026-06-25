using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using PaymentServices.RTPSend.Exceptions;
using PaymentServices.RTPSend.Exceptions.Core;
using PaymentServices.RTPSend.Helpers;
using PaymentServices.RTPSend.Interface.Adapters;
using PaymentServices.RTPSend.Models;

namespace PaymentServices.RTPSend.Functions;

public class GetPayment
{
    private readonly IProblemHelper _problemHelper;
    private readonly IPaymentCosmosDBAdapter _paymentCosmosDBService;
    private readonly ILogger<GetPayment> _log;

    public GetPayment(
        IPaymentCosmosDBAdapter paymentCosmosDBService,
        IProblemHelper problemHelper,
        ILogger<GetPayment> logger)
    {
        _paymentCosmosDBService = paymentCosmosDBService;
        _problemHelper = problemHelper;
        _log = logger;
    }

    [Function("GetPayment_evolveId")]
    [OpenApiOperation(operationId: "GetPayment_evolveId", tags: new[] { "GetPayment" })]
    [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
    [OpenApiParameter(name: "evolveId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The **evolveId** parameter")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(string), Description = "OK")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "evolveId/{evolveId}")] HttpRequest req,
        string evolveId)
    {
        _log.LogInformation("Request received to retrieve payment by evolve Id.");

        var headers = req.Headers;
        if (string.IsNullOrEmpty(headers["x-client-id"].ToString())
            || string.IsNullOrEmpty(headers["x-merchant-id"].ToString())
            || string.IsNullOrWhiteSpace(evolveId))
        {
            return BuildProblemResponse(req.HttpContext, new ValidationProblem());
        }

        var evolvePayment = (await _paymentCosmosDBService.GetPayment(evolveId, headers)).FirstOrDefault();

        if (evolvePayment is null)
        {
            return new NotFoundObjectResult(new ServiceErrorResponse
            {
                Error = "Not found",
                Message = $"Request with Evolve Id {evolveId} not found."
            });
        }

        return new OkObjectResult(evolvePayment);
    }

    [Function("GetPayment_paymentReference")]
    //[OpenApiOperation(operationId: "GetPayment_paymentReference", tags: new[] { "GetPayment" })]
    //[OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
    //[OpenApiParameter(name: "paymentReference", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The **paymentReference** parameter")]
    //[OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(string), Description = "OK")]
    public async Task<IActionResult> RunByReference(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "paymentReference/{paymentReference}")] HttpRequest req,
        string paymentReference)
    {
        _log.LogInformation("Request received to retrieve payment by payment reference.");

        var headers = req.Headers;
        if (string.IsNullOrEmpty(headers["x-client-id"].ToString())
            || string.IsNullOrEmpty(headers["x-merchant-id"].ToString())
            || string.IsNullOrWhiteSpace(paymentReference))
        {
            return BuildProblemResponse(req.HttpContext, new ValidationProblem());
        }

        var evolvePayment = (await _paymentCosmosDBService.GetPaymentByReference(paymentReference, headers)).FirstOrDefault();

        if (evolvePayment is null)
        {
            return new NotFoundObjectResult(new ServiceErrorResponse
            {
                Error = "Not found",
                Message = $"Request with Payment Reference {paymentReference} not found."
            });
        }

        return new OkObjectResult(evolvePayment);
    }

    private IActionResult BuildProblemResponse(HttpContext context, Problem problem)
    {
        var traceId = _problemHelper.GetTraceId(context);
        problem.TraceId = traceId;
        problem.ReferenceCode = _problemHelper.GenerateReferenceCode(traceId);
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
