using PaymentServices.RTPSend.Models.Domain;

namespace PaymentServices.RTPSend.Helpers;

public static class EnumHelper
{
    public static string GetEnumValue(RequestStatus status) => status.ToString();
}
