using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shared.Contracts.Responses;

namespace Veder.Tests;

/// <summary>Boots the real WebApi pipeline in-process so routing, mediator dispatch and JSON shape are exercised.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}

public sealed class ProvidersEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetProviders_ReturnsEverySeededProviderInPriorityOrder()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var providers = await response.Content.ReadFromJsonAsync<List<ProviderResponse>>();

        Assert.NotNull(providers);
        Assert.Equal(7, providers!.Count);
        Assert.Equal(
            ["open-meteo", "openweathermap", "weatherapi", "nws", "met.no", "openaq", "waqi"],
            providers.Select(p => p.Id));
        Assert.Equal("Open-Meteo", providers[0].DisplayName);
        Assert.Equal(1, providers[0].Priority);
    }

    [Fact]
    public async Task GetProviders_ReportsCapabilitiesWithoutTheNoneOrAllFlags()
    {
        using var client = factory.CreateClient();

        var providers = await client.GetFromJsonAsync<List<ProviderResponse>>("/api/providers");

        Assert.NotNull(providers);
        var openMeteo = providers!.Single(p => p.Id == "open-meteo");
        var openAq = providers.Single(p => p.Id == "openaq");

        Assert.Equal(
            ["CurrentWeather", "Forecast", "AirQuality", "Historical", "Geocoding", "Elevation", "Marine", "Ensemble", "Climate", "Flood"],
            openMeteo.Capabilities);
        Assert.Equal(["AirQuality"], openAq.Capabilities);
        Assert.DoesNotContain(openMeteo.Capabilities, c => c is "None" or "All");
    }

    [Fact]
    public async Task GetHealth_IsMappedAndReportsHealthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
