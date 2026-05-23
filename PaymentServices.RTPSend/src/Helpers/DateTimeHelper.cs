using System.Globalization;

namespace PaymentServices.RTPSend.Helpers;

public static class DateTimeHelper
{
    /// <summary>
    /// ISO-8601 timestamp without sub-second precision.
    /// </summary>
    public static string GetIsoTimestamp() =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// Round-trip ISO-8601 with full precision (e.g. 2024-05-13T20:54:00.1234567Z).
    /// Replaces Evolve.Digital.Core.Utilities.Datetime.DateTimeExtensions.ToCosmosDateTime.
    /// </summary>
    public static string ToCosmosDateTime(this DateTime dateTime) =>
        dateTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
}
