namespace Infrastructure.Providers.OpenMeteo;

/// <summary>
/// Configuration schema for the Open-Meteo provider, bound from the "OpenMeteo" configuration
/// section (appsettings, environment variables such as <c>OpenMeteo__ApiKey</c>, or user secrets).
/// </summary>
public sealed class OpenMeteoOptions
{
    public const string SectionName = "OpenMeteo";

    /// <summary>
    /// Optional commercial key. Free-tier calls need none; when set it is appended as the
    /// <c>apikey</c> query parameter and is never logged or persisted in clear text.
    /// </summary>
    public string? ApiKey { get; set; }

    public bool Enabled { get; set; } = true;

    public string Timezone { get; set; } = "auto";

    public string TemperatureUnit { get; set; } = "celsius";

    public string WindSpeedUnit { get; set; } = "kmh";

    public string PrecipitationUnit { get; set; } = "mm";

    public string CellSelection { get; set; } = "land";

    public int ForecastDays { get; set; } = 7;

    public int PastDays { get; set; }

    public int TimeoutSeconds { get; set; } = 20;

    public int MaxRetryAttempts { get; set; } = 3;

    public int MaxConcurrentRequests { get; set; } = 4;

    public string? Models { get; set; }

    public bool RecordCalls { get; set; } = true;

    public string? ContactEmail { get; set; }

    /// <summary>True when a commercial key is configured; free-tier calls work without one.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>A short, non-secret description of the active configuration for logs and operator output.</summary>
    public string Describe() =>
        $"enabled={Enabled}; timezone={Timezone}; units={TemperatureUnit}/{WindSpeedUnit}/{PrecipitationUnit}; " +
        $"forecastDays={ForecastDays}; pastDays={PastDays}; timeout={TimeoutSeconds}s; retries={MaxRetryAttempts}; " +
        $"concurrency={MaxConcurrentRequests}; models={(string.IsNullOrWhiteSpace(Models) ? "default" : Models)}; " +
        $"apiKey={(HasApiKey ? "configured" : "not configured (free tier)")}";

    /// <summary>Validates the schema so misconfiguration surfaces before the first HTTP call.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (ForecastDays is < 1 or > 16) problems.Add($"OpenMeteo:ForecastDays must be between 1 and 16 (got {ForecastDays}).");
        if (PastDays is < 0 or > 92) problems.Add($"OpenMeteo:PastDays must be between 0 and 92 (got {PastDays}).");
        if (TimeoutSeconds is < 1 or > 300) problems.Add($"OpenMeteo:TimeoutSeconds must be between 1 and 300 (got {TimeoutSeconds}).");
        if (MaxRetryAttempts is < 0 or > 10) problems.Add($"OpenMeteo:MaxRetryAttempts must be between 0 and 10 (got {MaxRetryAttempts}).");
        if (MaxConcurrentRequests is < 1 or > 32) problems.Add($"OpenMeteo:MaxConcurrentRequests must be between 1 and 32 (got {MaxConcurrentRequests}).");
        if (string.IsNullOrWhiteSpace(Timezone)) problems.Add("OpenMeteo:Timezone must be \"auto\", \"UTC\" or an IANA timezone name.");
        if (string.IsNullOrWhiteSpace(CellSelection)) problems.Add("OpenMeteo:CellSelection must be \"land\", \"sea\" or \"nearest\".");
        if (ContactEmail is not null && !ContactEmail.Contains('@')) problems.Add("OpenMeteo:ContactEmail must be a valid e-mail address when set.");

        return problems;
    }
}
