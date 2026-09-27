using Infrastructure.Helpers.DI;
using Scalar.AspNetCore;
using ServiceDefaults;
using UseCases.Helpers.DI;
using WebApi.Endpoints;
using WebApi.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();

// Explicit, not inherited from AddControllers(): the host calls UseAuthorization(), so the
// authorization services must be registered by name rather than as a side effect of MVC.
// The identity layer is additive and optional, but the pipeline is the same either way.
builder.Services.AddAuthorization();

// The cache is provider-scoped, so the host only has to supply an IDistributedCache:
// Redis/Garnet when Aspire wires a "garnet" connection string, in-memory otherwise.
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("garnet")))
{
    builder.AddRedisDistributedCache("garnet");
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOptions<WebApi.Endpoints.AggregationOptions>().Bind(builder.Configuration.GetSection(WebApi.Endpoints.AggregationOptions.SectionName));
builder.Services.AddOpenMeteoProvider(builder.Configuration);

// Identity is optional and additive: it never gates a weather endpoint.
var identityEnabled = builder.Services.AddVederIdentity(builder.Configuration);
builder.Services.AddUseCases();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapProvidersEndpoints();
app.MapWeatherEndpoints();
app.MapAggregatedWeatherEndpoints();

if (identityEnabled)
{
    app.MapVederIdentityEndpoints();
}

app.Run();

// Exposed so WebApplicationFactory<Program> can boot the real pipeline in integration tests.
public partial class Program { }
