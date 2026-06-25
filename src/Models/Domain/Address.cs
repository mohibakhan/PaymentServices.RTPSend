using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaymentServices.RTPSend.Models.Domain;

public class Address
{
    [JsonPropertyName("addressLines")]
    public IList<string>? AddressLines { get; set; }

    [JsonPropertyName("city")]
    [Description("Name of a built-up area, with defined boundaries, and a local government.")]
    public string? City { get; set; }

    [JsonPropertyName("county")]
    public string? County { get; set; }

    [JsonPropertyName("countryISOCode")]
    public string? CountryISOCode { get; set; }

    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("stateCode")]
    public string? StateCode { get; set; }
}

public class TabapayAddress
{
    [JsonPropertyName("line1")] public string? Line1 { get; set; }
    [JsonPropertyName("line2")] public string? Line2 { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("county")] public string? County { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("zipcode")] public string? Zipcode { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }

    /// <summary>
    /// Flattens an <see cref="Address"/> (multi-line address) into the TabaPay
    /// single-string-line shape.
    /// </summary>
    public static explicit operator TabapayAddress?(Address? src)
    {
        if (src is null) return null;

        var result = new TabapayAddress
        {
            City = src.City,
            County = src.County,
            State = src.StateCode,
            Zipcode = src.PostalCode,
            Country = src.CountryISOCode
        };

        if (src.AddressLines is { Count: > 0 })
        {
            result.Line1 = src.AddressLines[0];
            if (src.AddressLines.Count > 1)
                result.Line2 = src.AddressLines[1];
        }

        return result;
    }
}

[Description("Name. Exclusively use company or first, last.")]
public class AccountName
{
    [JsonPropertyName("company")]
    public string? Company { get; set; }

    [JsonPropertyName("first")]
    public string? First { get; set; }

    [JsonPropertyName("last")]
    public string? Last { get; set; }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(Company))
            return Company;
        return $"{First} {Last}".Trim();
    }
}

public class Phone
{
    [JsonPropertyName("number")]
    public string? Number { get; set; }
}

public class UltimateDebtor
{
    [JsonPropertyName("name")]
    [MaxLength(140)]
    [Description("Name")]
    public string? Name { get; set; }
}
