namespace Infrastructure.Providers.OpenMeteo;

/// <summary>Shared constants for the Open-Meteo provider.</summary>
public static class OpenMeteoProviderConstants
{
    public const string ProviderId = "open-meteo";

    public const string DisplayName = "Open-Meteo";

    /// <summary>Named <see cref="System.Net.Http.HttpClient"/> registered in DI, carrying the resilience pipeline.</summary>
    public const string HttpClientName = "open-meteo";

    /// <summary>Prefix for every emitted metric.</summary>
    public const string MetricPrefix = "veder.openmeteo";

    public const string ActivitySourceName = "Veder.OpenMeteo";
}
