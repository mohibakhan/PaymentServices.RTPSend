using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace PaymentServices.RTPSend.Helpers;

public interface IProblemHelper
{
    /// <summary>Returns the trace id for the current request.</summary>
    string? GetTraceId(HttpContext httpContext);

    /// <summary>Generates a 6-char base32 reference code derived from the trace id.</summary>
    string GenerateReferenceCode(string? traceId);

    /// <summary>Generates a random 6-char base32 reference code.</summary>
    string GenerateReferenceCode();
}

/// <summary>
/// Trace-id source changed from ApplicationInsights.RequestTelemetry to
/// <see cref="Activity.Current"/> — App Insights still gets it (the SDK uses
/// the same Activity in isolated worker), and we no longer need a direct
/// reference to Microsoft.ApplicationInsights.DataContracts.
/// </summary>
public sealed class ProblemHelper : IProblemHelper
{
    private const string Base32AllowedCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int ReferenceCodeLength = 6;

    [ExcludeFromCodeCoverage]
    public string? GetTraceId(HttpContext httpContext)
    {
        // 1. Prefer the current Activity's W3C trace id (set by App Insights instrumentation).
        var activity = Activity.Current;
        if (activity is not null)
            return activity.TraceId.ToString();

        // 2. Fall back to ASP.NET Core's per-request TraceIdentifier.
        return httpContext?.TraceIdentifier;
    }

    public string GenerateReferenceCode(string? traceId)
    {
        if (string.IsNullOrWhiteSpace(traceId))
            return GenerateReferenceCode();

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(traceId));
        return ToBase32String(hashBytes)[..ReferenceCodeLength];
    }

    public string GenerateReferenceCode()
    {
        var code = new char[ReferenceCodeLength];
        for (var i = 0; i < code.Length; i++)
        {
            var index = RandomNumberGenerator.GetInt32(0, Base32AllowedCharacters.Length);
            code[i] = Base32AllowedCharacters[index];
        }
        return new string(code);
    }

    private static string ToBase32String(byte[] input)
    {
        var bits = input
            .Select(b => Convert.ToString(b, 2).PadLeft(8, '0'))
            .Aggregate((a, b) => a + b)
            .PadRight((int)(Math.Ceiling(input.Length * 8 / 5d) * 5), '0');

        return Enumerable
            .Range(0, bits.Length / 5)
            .Select(i => Base32AllowedCharacters.Substring(Convert.ToInt32(bits.Substring(i * 5, 5), 2), 1))
            .Aggregate((a, b) => a + b);
    }
}
