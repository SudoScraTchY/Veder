using ServiceDefaults;
using WebApp.Components;

var builder = WebApplication.CreateBuilder(args);

// Service discovery, resilience, health checks and OpenTelemetry. Without this the UI is not really
// an Aspire resource: the AppHost's WithReference(api) never reaches this process.
builder.AddServiceDefaults();

// Where the aggregated API lives. The default is Aspire service discovery, NOT a configured port:
// "api" is the resource name declared in AppHost.cs (AddProject<Projects.WebApi>("api")) and the
// discovery handler is registered above, so the real endpoint is resolved from the running
// orchestrator at request time. ApiBaseUrl is an explicit override for standalone runs only, where
// no AppHost is publishing an endpoint for "api".
const string serviceDiscoveryAddress = "https+http://api";
var configuredApiBaseUrl = builder.Configuration["ApiBaseUrl"];
var usingOverride = !string.IsNullOrWhiteSpace(configuredApiBaseUrl);
var apiBaseUrl = usingOverride ? configuredApiBaseUrl! : serviceDiscoveryAddress;

builder.Services.AddHttpClient("api", client => client.BaseAddress = new Uri(apiBaseUrl));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

// Logged so that "which API is this UI talking to, and why" is never a guess again.
app.Logger.LogInformation(
    "Veder UI: aggregated API base address is {ApiBaseUrl} ({ApiBaseUrlSource}).",
    apiBaseUrl,
    usingOverride
        ? "ApiBaseUrl configuration override"
        : "Aspire service discovery for the AppHost resource \"api\"");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WebApp.Client._Imports).Assembly);

app.Run();