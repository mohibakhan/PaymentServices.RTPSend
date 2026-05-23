using System.Reflection;
using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Helpers.ISO3166;

/// <summary>
/// One row of ISO 3166-1 country data merged with UN Geoscheme regional codes.
/// Source: https://github.com/lukes/ISO-3166-Countries-with-Regional-Codes
/// </summary>
public class CountryCode
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("alpha-2")] public string Alpha2 { get; set; } = string.Empty;
    [JsonPropertyName("alpha-3")] public string Alpha3 { get; set; } = string.Empty;
    [JsonPropertyName("country-code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("iso_3166-2")] public string Iso31662 { get; set; } = string.Empty;
    [JsonPropertyName("region")] public string? Region { get; set; }
    [JsonPropertyName("sub-region")] public string? SubRegion { get; set; }
    [JsonPropertyName("intermediate-region")] public string? IntermediateRegion { get; set; }
    [JsonPropertyName("region-code")] public string? RegionCode { get; set; }
    [JsonPropertyName("sub-region-code")] public string? SubRegionCode { get; set; }
    [JsonPropertyName("intermediate-region-code")] public string? IntermediateRegionCode { get; set; }
}
