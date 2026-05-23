using System.Diagnostics.CodeAnalysis;
using System.Net;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions.Core;

namespace PaymentServices.RTPSend.Exceptions;

[ExcludeFromCodeCoverage]
public class UnexpectedProblem : Problem
{
    public UnexpectedProblem()
    {
        Type = new Uri(ProblemTypes.Base + "unexpected");
        Title = "Unexpected Problem";
        Detail = "Something unexpected happened.  Please report this error to our support team with the 'referenceCode'";
        Status = (int)HttpStatusCode.InternalServerError;
    }
}
