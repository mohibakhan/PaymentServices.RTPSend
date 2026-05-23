using System.Diagnostics.CodeAnalysis;
using System.Net;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions.Core;

namespace PaymentServices.RTPSend.Exceptions;

[ExcludeFromCodeCoverage]
public class ForbiddenProblem : Problem
{
    public ForbiddenProblem()
    {
        Type = new Uri(ProblemTypes.Base + "forbidden");
        Title = "Forbidden Problem";
        Detail = "The request cannot be processed. Please report this error to our support team with the 'referenceCode'";
        Status = (int)HttpStatusCode.Forbidden;
    }
}
