using Domain.Entities.Interfaces;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Credentials;
using Infrastructure.Persistence.Json;
using Infrastructure.Providers;
using Infrastructure.Providers.OpenMeteo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Helpers.DI;

/// <summary>Registers the Open-Meteo provider: options, credential vault, HTTP transport with a resilience pipeline, and every capability adapter.</summary>
public static class OpenMeteoServiceCollectionExtensions
{
    public static IServiceCollection AddOpenMeteoProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpenMeteoOptions>()
            .Bind(configuration.GetSection(OpenMeteoOptions.SectionName))
            .Validate(options => options.Validate().Count == 0,
                "OpenMeteo configuration is invalid; see OpenMeteoOptions.Validate for the accepted ranges.");

        services.AddOptions<ProviderStoreOptions>()
            .Bind(configuration.GetSection(ProviderStoreOptions.SectionName));

        services.AddSingleton<OpenMeteoTelemetry>();
        services.AddSingleton<IProviderCallRecordStore, JsonProviderCallRecordStore>();
        services.AddSingleton<IProviderDiagnosticsStore, JsonProviderDiagnosticsStore>();
        services.AddSingleton<IObservationStore, JsonObservationStore>();
        services.AddSingleton<IProviderCredentialStore, ProtectedCredentialStore>();
        services.AddSingleton<ProviderCallRecorder>();
        services.AddSingleton<OpenMeteoClient>();
        services.AddSingleton<OpenMeteoWeatherProvider>();
        services.AddSingleton<OpenMeteoAirQualityProvider>();
        services.AddSingleton<OpenMeteoExtendedProvider>();

        // Enforces OpenMeteo:MaxConcurrentRequests instead of merely documenting it.
        services.AddSingleton(sp => new ProviderConcurrencyGate(
            sp.GetRequiredService<IOptions<OpenMeteoOptions>>().Value.MaxConcurrentRequests));

        // A key stored in the vault wins over configuration when configuration has none, so an
        // operator can add or rotate a credential without touching source or appsettings.
        services.AddSingleton<IPostConfigureOptions<OpenMeteoOptions>, OpenMeteoCredentialPostConfigure>();

        // The provider abstraction is consumed through IWeatherProvider / IAirQualityProvider, so each
        // concrete adapter is also published under its capability interface.
        services.AddSingleton<IWeatherProvider>(sp => sp.GetRequiredService<OpenMeteoWeatherProvider>());
        services.AddSingleton<IWeatherSeriesProvider>(sp => sp.GetRequiredService<OpenMeteoWeatherProvider>());
        services.AddSingleton<IAirQualityProvider>(sp => sp.GetRequiredService<OpenMeteoAirQualityProvider>());
        services.AddSingleton<IHistoricalWeatherProvider>(sp => sp.GetRequiredService<OpenMeteoExtendedProvider>());
        services.AddSingleton<IMarineWeatherProvider>(sp => sp.GetRequiredService<OpenMeteoExtendedProvider>());
        services.AddSingleton<IEnsembleWeatherProvider>(sp => sp.GetRequiredService<OpenMeteoExtendedProvider>());
        services.AddSingleton<IClimateProjectionProvider>(sp => sp.GetRequiredService<OpenMeteoExtendedProvider>());
        services.AddSingleton<IFloodForecastProvider>(sp => sp.GetRequiredService<OpenMeteoExtendedProvider>());
        services.AddSingleton<IElevationProvider>(sp => sp.GetRequiredService<OpenMeteoExtendedProvider>());
        services.AddSingleton<IGeocodingProvider>(sp => sp.GetRequiredService<OpenMeteoExtendedProvider>());

        services.AddHttpClient(OpenMeteoProviderConstants.HttpClientName, (sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<OpenMeteoOptions>>().Value;
                http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Veder/1.0 (+https://github.com/veder)");

                if (!string.IsNullOrWhiteSpace(options.ContactEmail))
                {
                    http.DefaultRequestHeaders.Add("From", options.ContactEmail);
                }
            })
            .AddStandardResilienceHandler(options =>
            {
                // Open-Meteo answers bursts with 429, so the retry policy has to honour Retry-After.
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.UseJitter = true;
                options.Retry.Delay = TimeSpan.FromMilliseconds(400);
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Result is { StatusCode: System.Net.HttpStatusCode.TooManyRequests or
                        System.Net.HttpStatusCode.RequestTimeout or
                        System.Net.HttpStatusCode.InternalServerError or
                        System.Net.HttpStatusCode.BadGateway or
                        System.Net.HttpStatusCode.ServiceUnavailable or
                        System.Net.HttpStatusCode.GatewayTimeout } ||
                    args.Outcome.Exception is HttpRequestException or TaskCanceledException);

                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });

        // The client resolves the named HttpClient rather than a bare one, so the pipeline above applies.
        services.AddSingleton(sp => new OpenMeteoClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OpenMeteoProviderConstants.HttpClientName),
            sp.GetRequiredService<IOptions<OpenMeteoOptions>>(),
            sp.GetRequiredService<OpenMeteoTelemetry>(),
            sp.GetRequiredService<ILogger<OpenMeteoClient>>(),
            sp.GetRequiredService<ProviderCallRecorder>(),
            sp.GetRequiredService<ProviderConcurrencyGate>()));

        return services;
    }
}

/// <summary>Fills <see cref="OpenMeteoOptions.ApiKey"/> from the credential vault when configuration does not supply one.</summary>
public sealed class OpenMeteoCredentialPostConfigure(IProviderCredentialStore credentials) : IPostConfigureOptions<OpenMeteoOptions>
{
    public void PostConfigure(string? name, OpenMeteoOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return;
        }

        if (credentials.TryResolve(OpenMeteoProviderConstants.ProviderId, out var stored) && !string.IsNullOrWhiteSpace(stored))
        {
            options.ApiKey = stored;
        }
    }
}
