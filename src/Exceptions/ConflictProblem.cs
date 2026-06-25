using System.Diagnostics.CodeAnalysis;
using System.Net;
using PaymentServices.RTPSend.Constants;
using PaymentServices.RTPSend.Exceptions.Core;

namespace PaymentServices.RTPSend.Exceptions;

[ExcludeFromCodeCoverage]
public class ConflictProblem : Problem
{
    public ConflictProblem()
    {
        Type = new Uri(ProblemTypes.Base + "conflict");
        Title = "Conflict Problem";
        Detail = "The request cannot be processed. The payment reference sent with this request already exists.";
        Status = (int)HttpStatusCode.Conflict;
    }
}
