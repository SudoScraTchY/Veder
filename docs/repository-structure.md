# Repository structure

An annotated map of what is on disk today. Each entry names the directory's purpose and where
execution starts.

Convention: `entry point` means the file you open first to understand the directory, not necessarily
a `Main` method.

- [Root files](#root-files)
- [AppHost/](#apphost)
- [Domain/](#domain)
- [UseCases/](#usecases)
- [Shared/](#shared)
- [Infrastructure/](#infrastructure)
- [ServiceDefaults/](#servicedefaults)
- [WebApi/](#webapi)
- [WebApp/](#webapp)
- [tests/Veder.Tests/](#testsvedertests)
- [tools/](#tools)
- [docs/](#docs)
- [Empty or vestigial directories](#empty-or-vestigial-directories)

## Root files

| Path | Purpose |
| --- | --- |
| `Veder.Server.slnx` | The solution. XML-based `.slnx` format; every project listed below appears here. |
| `README.md` | Entry point for a newcomer: quickstart, prerequisites, layout, troubleshooting. |
| `.gitignore` | Ignores build output, `*.DotSettings.user`, and the downloaded Tailwind binary at `tools/tailwind/tailwindcss.exe`. |
| `.dockerignore` | Keeps build output and IDE files out of any container build context. |
| `Veder.Server.sln.DotSettings.user` | Rider/ReSharper local settings; ignored by `.gitignore` and present only on developer machines. |

No `LICENSE`, `CONTRIBUTING`, `CODE_OF_CONDUCT`, `CHANGELOG` or CI workflow file exists yet — see the
[README](../README.md#contributing-and-licence).

## AppHost/

The Aspire orchestrator. It starts the cache, the database, the API and the UI, and wires the
references between them so no endpoint has to be configured by hand.

| File | Purpose |
| --- | --- |
| `AppHost.cs` | **Entry point.** Declares `garnet`, `mongo`/`veder`, `api` (the API project, resource name **`api`**) and `webapp` (the UI, `.WithReference(api)`). |
| `AppHost.csproj` | `Aspire.AppHost.Sdk/13.4.6`; references the API and UI projects. |
| `appsettings.json`, `appsettings.Development.json` | Logging levels, including `Aspire.Hosting.Dcp` reduced to `Warning`. |

The resource name `api` is load-bearing: the UI resolves it through service discovery
(`https+http://api`), so renaming it here would break the UI's API address.

## Domain/

The model and the rules. No HTTP, no storage, no provider SDKs — see
[architecture.md §3](architecture.md#3-layers-and-dependency-direction).

| Path | Purpose |
| --- | --- |
| `Entities/` | Aggregates and records: `WeatherForecast`, `WeatherSeries`/`SeriesPoint`, `GeoLocation`, `ProviderCallRecord`, `ProviderDiagnostics`, `ObservationRecord`, `ProviderCredentialMetadata`. |
| `Entities/Interfaces/` | Every provider and storage contract: the capability interfaces (`IGeocodingProvider`, `IElevationProvider`, `IMarineWeatherProvider`, `IEnsembleWeatherProvider`, `IClimateProjectionProvider`, `IFloodForecastProvider`, `IAirPollutionProvider`, `IHistoricalWeatherProvider`), the read contracts (`IWeatherProvider`, `IWeatherSeriesProvider`), the stores (`IObservationStore`, `IProviderCallRecordStore`, `IProviderDiagnosticsStore`, `IProviderCredentialStore`) and the registry/selector contracts. |
| `Entities/ValueObjects/`, `Entities/Exceptions/`, `Entities/Enumerations/` | Value objects, domain exceptions (`ProviderAuthenticationException`, `ProviderRequestException`, `ProviderRateLimitedException`), and the `ProviderCapability` flags enum. |
| `Caching/` | The caching rules: `GeoBucket.cs` (grid flooring, neighbourhood, longitude wrap, polar widening), `WeatherCacheKey.cs` (key composition and `CacheTtlPolicy`), `ProviderFailurePolicy.cs` (failure classification and substitution permission), `IWeatherCacheStore.cs` (store contract with outcomes and statistics). |
| `Common/`, `ValueObjects/`, `Enums/`, `Events/`, `Helpers/` | Base types, shared value objects and helpers carried over from the original solution structure. |

## UseCases/

The application layer: what the system can be asked to do, expressed as commands, queries and
handlers dispatched by Cortex.Mediator.

| Path | Purpose |
| --- | --- |
| `Cases/` (`Weather`, `AirQuality`, `Locations`) | Case definitions grouped by subject. |
| `Handlers/` (`Commands`, `Queries`) | Mediator handlers implementing those cases. |
| `Behaviors/` | Pipeline behaviours (cross-cutting concerns around handlers). |
| `Services/` | Application services that do not belong to a single case. |
| `Helpers/DI/DependencyInjection.cs` | **Entry point** for wiring the layer: `AddUseCases()`. |

## Shared/

Contracts that cross layer boundaries, plus the result and mapping helpers used on both sides.

| Path | Purpose |
| --- | --- |
| `Contracts/Commands`, `Contracts/Queries` | Request contracts. |
| `Contracts/Dto` | Data-transfer shapes. |
| `Contracts/Requests`, `Contracts/Responses` | HTTP-facing contracts, including `AggregatedWeatherResponse.cs` (the aggregated envelope with its `ResponseProvenance` block) and `WeatherResponses.cs`. |
| `Helpers/` | Mapping and helper code shared by `UseCases` and the hosts. |

## Infrastructure/

Every adapter: providers, persistence, caching, Identity. This is where the domain contracts meet the
outside world.

| Path | Purpose |
| --- | --- |
| `Providers/OpenMeteo/` | **Entry point:** `openmeteoclient.cs` (HTTP), then `openmeteorequestbuilder.cs`, `openmeteoresponseparser.cs`, `openmeteomapper.cs`, and the three providers: `openmeteoweatherprovider.cs`, `openmeteoairqualityprovider.cs`, `openmeteoextendedprovider.cs` (the seven capability interfaces). `openmeteooptions.cs` holds configuration and `openmeteooptions.Validate()`. |
| `Providers/ProviderRegistry.cs`, `ProviderSelector.cs` | Which providers exist with which capabilities and priority; which one answers a given request. |
| `Providers/ProviderCallRecorder.cs`, `ProviderConcurrencyGate.cs` | Call accounting and the per-process concurrency limit. |
| `Providers/Weather/`, `Providers/AirQuality/` | Provider interfaces kept beside their adapters (`IWeatherProvider.cs`, `IAirPollutionProvider.cs`). |
| `Caching/InMemoryWeatherCacheStore.cs` | The cache store: single-flight, stale-on-failure grace, prefix invalidation, counters. |
| `Cache/CacheKeyBuilder.cs`, `Cache/DistributedCacheService.cs` | The earlier distributed-cache path: key building and a `IDistributedCache`-based service. |
| `Persistence/Json/` | Durable JSON stores: `jsonprovidercallrecordstore.cs`, `jsonproviderdiagnosticsstore.cs`, `JsonObservationStore.cs`, `jsonrecordfile.cs`, `providerstoremanifest.cs`. |
| `Persistence/Credentials/protectedcredentialstore.cs` | Credential vault (DPAPI on Windows, AES-256-GCM fallback). |
| `Persistence/InMemory/InMemorySavedLocationRepository.cs` | Saved locations, in memory. |
| `Persistence/providerstoreoptions.cs` | Where the store lives (`ProviderStore:RootPath`, default `%LOCALAPPDATA%\Veder\provider-store`). |
| `Identity/VederIdentityDbContext.cs`, `Identity/Migrations/` | The accounts schema (`identity`) and its migrations history table `__identity_migrations`. |
| `Data/Contexts`, `Data/Configurations` | EF Core context and entity configurations. |
| `Helpers/DI/DependencyInjection.cs`, `Helpers/DI/OpenMeteoServiceCollectionExtensions.cs` | **Entry point** for wiring the layer: `AddInfrastructure(configuration)` and `AddOpenMeteoProvider(configuration)`. |
| `Services/` | See [empty or vestigial directories](#empty-or-vestigial-directories). |

## ServiceDefaults/

The Aspire shared project — the one place where service-to-service concerns are configured.

| File | Purpose |
| --- | --- |
| `Extensions.cs` | **Entry point.** `AddServiceDefaults()` (service discovery, `AddStandardResilienceHandler`, health checks, OpenTelemetry) and `MapDefaultEndpoints()` (`/health`, `/alive`, Development only). |
| `ServiceDefaults.csproj` | `IsAspireSharedProject`; referenced by `WebApi` and `WebApp`. |

## WebApi/

The HTTP host for the aggregated weather API, provider administration and the optional accounts
endpoints.

| File | Purpose |
| --- | --- |
| `Program.cs` | **Entry point.** Adds service defaults, OpenAPI, authorization, the cache (Garnet when `ConnectionStrings:garnet` exists, otherwise in-memory), infrastructure and use cases, the Open-Meteo provider, and optional Identity; maps the endpoint groups. |
| `Endpoints/AggregatedWeatherEndpoints.cs` | The aggregated endpoint (`GET /api/weather/aggregate`) plus `AggregationOptions` (`Aggregation` section). |
| `Endpoints/ProvidersEndpoints.cs` | `GET /api/providers` — capabilities, priority and health. |
| `Endpoints/WeatherEndpoints.cs` | Capability-scoped endpoints: forecast, air quality, marine, ensemble, climate, flood, elevation, geocoding. |
| `Identity/identityregistration.cs` | Registers the Identity store and endpoints **only when `ConnectionStrings:VederIdentity` is present**; cookie events return 401/403 for `/api` instead of redirecting. |
| `Properties/launchSettings.json` | Launch profiles: `http` → `5680`, `https` → `5681;5680`. |
| `appsettings.json`, `appsettings.Development.json` | Logging only. No endpoints, no credentials. |
| `WebApi.http` | Manual request file: `/health`, the aggregate endpoint, providers. |
| `DTOs/`, `Extensions/`, `Middleware/` | Empty — see [empty or vestigial directories](#empty-or-vestigial-directories). |

## WebApp/

The Blazor web interface, plus its WebAssembly companion project.

| Path | Purpose |
| --- | --- |
| `WebApp/Program.cs` | **Entry point.** Adds service defaults, resolves the API address (`ApiBaseUrl` override, otherwise service discovery for `api`), registers the `api` HTTP client, and maps Razor components. |
| `WebApp/Components/App.razor` | Root document: stylesheets, script references, `HeadOutlet`, `Routes`. |
| `WebApp/Components/Routes.razor` | Router configuration and the not-found route. |
| `WebApp/Components/Layout/` | `MainLayout.razor` (shell, skip-to-content link, error banner), `NavMenu.razor` (all five screens), `ReconnectModal.razor` (Blazor disconnect UI). |
| `WebApp/Components/Pages/` | `Home.razor` (search), `Weather.razor` (the reading; routes `/weather`, `/{Place}`, `/{Latitude:double},{Longitude:double}`), `Providers.razor` (`/providers`), `About.razor` (`/about`), `Account.razor` (`/account`), `Error.razor`, `NotFound.razor`. |
| `WebApp/Components/Ui/` | `ProvenanceChip.razor` (semantic source chip with accessible text) and `StateBlock.razor` (loading/empty/error/ready). |
| `WebApp/Routing/PlaceRoute.cs` | The canonical URL shape: `ForPlace`, `ForCoordinates`, `ToSlug`, `ToDisplayName`, culture-invariant coordinate formatting. |
| `WebApp/wwwroot/` | `app.tailwind.css` (design tokens and component classes), `app.css` (generated, committed), `lib/` (vendored browser libraries). |
| `WebApp/WebApp.csproj` | Includes the `BuildTailwind` target that regenerates `app.css` before build when the CLI is present. |
| `WebApp.Client/` | The WebAssembly project (`WebApp.Client.csproj`, `Pages/`, `wwwroot/`). Registered and referenced, but no page opts into the WebAssembly render mode yet. |

## tests/Veder.Tests/

A single flat xUnit project (109 tests). `Fakes.cs` holds the provider and store doubles; the rest map
one-to-one onto a component:

| File | Covers |
| --- | --- |
| `OpenMeteoTests.cs` | Wire layer: endpoints, request building, response parsing, mapping. |
| `CacheKeyTests.cs`, `CacheKeyBuilderTests.cs` | Key composition and the TTL/jitter policy. |
| `WeatherCacheStoreTests.cs` | Hit/miss/coalescing, stale-on-failure, invalidation. |
| `ProviderFailurePolicyTests.cs` | Failure classification and substitution permission. |
| `ProviderRegistryTests.cs`, `ProviderSelectorTests.cs` | Registry contents and selection order. |
| `AggregatedWeatherComposerTests.cs` | The aggregated envelope and its provenance. |
| `ProvidersEndpointTests.cs`, `WeatherEndpointTests.cs` | Endpoints through the real pipeline (`WebApplicationFactory`). |
| `CredentialAndObservationTests.cs` | Credential lifecycle and observation idempotency. |
| `HandlerMappingTests.cs`, `SavedLocationRepositoryTests.cs` | Mediator wiring and the saved-location repository. |

The project file is tracked as `veder.tests.csproj` (lowercase) while the solution references
`Veder.Tests.csproj` — see [decisions/0006](decisions/0006-portability-and-naming.md).

## tools/

| Path | Purpose |
| --- | --- |
| `Veder.ProviderProbe/program.cs` | **Entry point.** Console tool that exercises every provider capability through the product's own code path, manages credential lifecycle (`credentials set\|rotate\|revoke\|status`), prints observations, and accepts `VEDER_PROVIDER_STORE` to relocate the provider store. |
| `Veder.ProviderProbe/veder.providerprobe.csproj` | The probe's project file (lowercase name, as tracked). |
| `tailwind/` | Home of the downloaded Tailwind standalone CLI (`tailwindcss.exe`, ~112 MB, gitignored). Absent until a developer downloads it; the build warns and falls back to the committed `app.css`. |

## docs/

| Path | Purpose |
| --- | --- |
| `README.md` | Documentation index and operations guide (layout, running, configuration, cache strategy, failure handling, endpoints, Identity, front end, runbook, tests). |
| `architecture.md` | Components, interfaces, data flow, topology. |
| `stack.md` | Dependency inventory with versions and reasons. |
| `repository-structure.md` | This file. |
| `setup.md` | Clone → running instance, configuration reference, troubleshooting. |
| `decisions/` | Decision records (`0001`–`0006`). |
| `providers/open-meteo.md` | The Open-Meteo implementation and its 32-row capability matrix. |
| `ui-design-brief.md`, `ui-handoff.md`, `ui-accessibility.md` | Interface: screens and flow, handoff, measured contrast. |
| `runbook-startup-and-ports.md` | The port-exclusion diagnosis and fix. |

## Empty or vestigial directories

These exist on disk but contain no files. They are listed so a reader does not assume something is
missing from this document:

`WebApi/DTOs/`, `WebApi/Extensions/`, `WebApi/Middleware/`, `Infrastructure/Services/`.

They are leftovers from the original solution skeleton; the corresponding functionality lives in
`Shared/Contracts` (`DTOs`), the endpoint files (`Extensions`), and the ASP.NET pipeline in
`Program.cs` (`Middleware`). Removing them is a cosmetic change and is not part of this documentation
work.
