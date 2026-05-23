using System.Reflection;
using System.Text.Json;

namespace PaymentServices.RTPSend.Helpers.ISO3166;

/// <summary>
/// ISO-3166-1 country code list. Replaces
/// Evolve.Digital.Shared.Helpers.ISO3166.CountryCodeHelper.
/// JSON file is embedded as a resource at build time, so the lookup is
/// in-memory and offline-safe.
/// </summary>
public static class CountryCodeHelper
{
    private const string ResourceName = "PaymentServices.RTPSend.Helpers.ISO3166.iso3166_all.json";

    private static readonly Lazy<Task<IReadOnlyList<CountryCode>>> _lazyList =
        new(LoadAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Task<IReadOnlyList<CountryCode>> GetListAsync() => _lazyList.Value;

    public static async Task<CountryCode?> GetForCodeAsync(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return null;

        var upper = countryCode.ToUpperInvariant();
        var list = await GetListAsync();
        return list.FirstOrDefault(c =>
            c.Code == upper || c.Alpha2 == upper || c.Alpha3 == upper);
    }

    public static async Task<string?> GetAlpha2CodeAsync(string countryCode) =>
        (await GetForCodeAsync(countryCode))?.Alpha2;

    public static async Task<string?> GetAlpha3CodeAsync(string countryCode) =>
        (await GetForCodeAsync(countryCode))?.Alpha3;

    public static async Task<string?> GetCountryCodeAsync(string countryCode) =>
        (await GetForCodeAsync(countryCode))?.Code;

    private static async Task<IReadOnlyList<CountryCode>> LoadAsync()
    {
        try
        {
            var assembly = typeof(CountryCodeHelper).Assembly;
            await using var stream = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{ResourceName}' not found. Make sure the csproj " +
                    $"includes <EmbeddedResource Include=\"Helpers\\ISO3166\\iso3166_all.json\" />.");

            var list = await JsonSerializer.DeserializeAsync<List<CountryCode>>(stream)
                ?? new List<CountryCode>();
            return list;
        }
        catch
        {
            return Array.Empty<CountryCode>();
        }
    }
}
