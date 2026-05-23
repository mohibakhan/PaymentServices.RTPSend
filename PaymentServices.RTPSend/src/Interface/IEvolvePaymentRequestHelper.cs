using Microsoft.AspNetCore.Http;
using PaymentServices.RTPSend.Models.Cosmos;
using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Interface;

public interface IEvolvePaymentRequestHelper
{
    /// <summary>
    /// Converts an incoming <see cref="BasicPaymentRequest"/> into the persisted
    /// <see cref="EvolvePaymentRequest"/> Cosmos document, populating headers,
    /// initial status, and defaulted RTP fields.
    /// </summary>
    EvolvePaymentRequest ConvertBasicToEvolveRequest(
        BasicPaymentRequest basicPaymentRequest,
        IHeaderDictionary headers,
        string documentSubType);
}
