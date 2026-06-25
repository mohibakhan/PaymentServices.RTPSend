using System.Diagnostics.CodeAnalysis;
using System.Net;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions.Core;

namespace PaymentServices.RTPSend.Exceptions;

[ExcludeFromCodeCoverage]
public class ValidationProblem : Problem
{
    public ValidationProblem()
    {
        Type = new Uri(ProblemTypes.Base + "validation");
        Title = "Validation Problem";
        Detail = "The request did not pass validation.  Make sure request is not null and all required fields and headers are present.";
        Status = (int)HttpStatusCode.BadRequest;
    }
}
