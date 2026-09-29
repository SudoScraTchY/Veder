# Veder

Weather and air-quality service: one HTTP call returns current conditions, a daily outlook and air
quality for any place — and names the provider that actually answered.

It is built for the deployment at `veder.ir`, where **the domain is the product**: a place is a path
(`/Tehran`, `/35.6892,51.389`), not a query string.

Two applications live here:

- **`WebApi`** — the aggregated weather API, plus provider administration endpoints.
- **`WebApp`** — a Blazor web interface that reads that API and renders the reading.

Everything runs locally without paid services: the default provider (Open-Meteo) needs no API key.

## Who it is for

Someone who wants a weather reading they can trust and inspect: the response carries the source, the
cache outcome and the age of the data, so a substituted provider is visible instead of silent. The
UI is deliberately calm and legible rather than decorative, and it works from 360 px upwards.

## Quickstart

Verified on Windows with .NET SDK 10 (see [Prerequisites](#prerequisites)):

```powershell
git clone https://github.com/SudoScraTchY/Veder.git
cd Veder.Server

# 1. build
dotnet build Veder.Server.slnx

# 2. run the API (http://localhost:5680)
dotnet run --project WebApi --launch-profile http

# 3. in a second terminal: point the UI at that API (http://localhost:5690)
$env:ApiBaseUrl = 'http://localhost:5680'
dotnet run --project WebApp/WebApp --launch-profile http
```

Then open <http://localhost:5690/Tehran>. Expect current conditions, a seven-day outlook, air
quality, and a provenance chip naming the provider — for example `open-meteo · Hit · 12s`.

Prefer one process to orchestrate both? See [Run everything under Aspire](#run-everything-under-aspire).

## Prerequisites

| Requirement | Version used here | Notes |
| --- | --- | --- |
| .NET SDK | 10.0.301 | `net10.0` throughout. There is no `global.json`, so any 10.x SDK works. |
| .NET runtime | 10.0.9 | Ships with the SDK. |
| Docker (optional) | any recent engine | Only for `dotnet run --project AppHost`, which starts Garnet and MongoDB containers. |
| Node.js | **not required** | Tailwind is a standalone binary; see [Styling](docs/setup.md#styling-tailwind-v4-without-node). |

If `dotnet` resolves to a runtime-only shim on your machine, call the SDK explicitly
(`& 'C:\Program Files\dotnet\dotnet.exe' …`) and set `DOTNET_ROOT`. Symptom of a missing SDK:
`dotnet build` reports that no SDK can be found.

## Repository layout

```
AppHost/            Aspire orchestration: Garnet, MongoDB, api, webapp
Domain/             Pure domain model, caching rules, provider contracts
UseCases/           Application cases, handlers, pipeline behaviours
Infrastructure/     Provider adapters (Open-Meteo), persistence, cache, Identity
Shared/             Contracts crossing layer boundaries (requests, responses, DTOs)
ServiceDefaults/    Aspire service defaults: discovery, resilience, health, OpenTelemetry
WebApi/             HTTP host: aggregated weather API, providers, optional Identity
WebApp/             Blazor web interface (WebApp.Client is the WebAssembly project)
tests/Veder.Tests/  xUnit suite (109 tests)
tools/              Provider probe and the Tailwind build step
docs/               Documentation set — start at docs/README.md
```

Each entry is described in full in
[docs/repository-structure.md](docs/repository-structure.md).

## Documentation

| Document | Contents |
| --- | --- |
| [docs/README.md](docs/README.md) | Documentation index and operations guide |
| [docs/architecture.md](docs/architecture.md) | Purpose, components, interfaces, data flow, topology |
| [docs/stack.md](docs/stack.md) | Every framework and dependency, its version and why it is there |
| [docs/repository-structure.md](docs/repository-structure.md) | Annotated map of every directory and entry point |
| [docs/setup.md](docs/setup.md) | Clone → running instance, configuration, environment reference |
| [docs/decisions/](docs/decisions/) | Decision records: context, decision, consequences |
| [docs/providers/open-meteo.md](docs/providers/open-meteo.md) | The provider implementation and its capability matrix |
| [docs/runbook-startup-and-ports.md](docs/runbook-startup-and-ports.md) | Diagnosing the port trap that reads like a crash |
| [docs/ui-handoff.md](docs/ui-handoff.md) | Interface handoff: screens, decisions, what is not wired |

## Testing

```powershell
dotnet test tests/Veder.Tests/veder.tests.csproj
```

Current suite: **109 tests, all passing**, covering the provider wire layer, the cache key and store,
the failure policy, the aggregated composer, the registry/selector, and the HTTP endpoints through
`WebApplicationFactory`.

## Run everything under Aspire

```powershell
dotnet run --project AppHost
```

`AppHost` starts Garnet (cache), MongoDB, the API and the UI, and wires them with references — the UI
receives the API endpoint through **service discovery** (`https+http://api`) rather than a configured
port, and prints which strategy it used at startup. Requires a running container runtime.

## Configuration essentials

Values are supplied by the environment, user-secrets or the Aspire AppHost — never committed. The
names you are most likely to need:

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__VederIdentity` | Enables accounts against PostgreSQL. **Absent → the app still boots**, the identity endpoints are simply not mapped. |
| `ConnectionStrings__garnet` | Supplied by Aspire. Absent → an in-memory distributed cache is used instead. |
| `ApiBaseUrl` | Explicit API address for the UI in standalone runs. Absent under Aspire → service discovery. |
| `ProviderStore__RootPath` | Where provider records are persisted (default `%LOCALAPPDATA%\Veder\provider-store`). The provider probe also accepts `VEDER_PROVIDER_STORE` and maps it to this key. |
| `OpenMeteo__ApiKey` | Optional commercial Open-Meteo key. The free tier needs none. |

The full reference, including every `OpenMeteo__*` and `Aggregation__*` key, is in
[docs/setup.md](docs/setup.md#configuration-reference).

## Troubleshooting

**The host prints its startup banner and then dies.** On Windows, listen ports can fall inside an
OS-excluded TCP range; binding them fails with `SocketException (10013)` and Kestrel treats that as
fatal. Check with `netsh int ipv4 show excludedportrange protocol=tcp`, or run
`tools/check-ports.ps1`. Full analysis: [docs/runbook-startup-and-ports.md](docs/runbook-startup-and-ports.md).

**The UI shows "Could not load weather data".** It cannot reach the API. In standalone runs set
`ApiBaseUrl`; under Aspire, check the API resource is running.

**The solution will not build on Linux or macOS.** The solution file references
`tests/Veder.Tests/Veder.Tests.csproj`, while the tracked file is `veder.tests.csproj`. Windows
resolves this case-insensitively; other filesystems do not. See
[docs/decisions/0006-portability-and-naming.md](docs/decisions/0006-portability-and-naming.md).

**An unknown place returns HTTP 200 with an empty state** rather than a 404 (a soft 404). Known
limitation, documented in [docs/decisions/0005-url-scheme.md](docs/decisions/0005-url-scheme.md).

## Contributing and licence

No `LICENSE`, `CONTRIBUTING` or `CODE_OF_CONDUCT` file exists in this repository yet — if you intend
to accept outside contributions, add them before publishing. Until then, treat the code as
all-rights-reserved by default.

## Keeping these documents current

Every document in `docs/` is owned by whoever changes the code it describes: a change to an
interface, a dependency version or a run command is not complete until the matching document is
updated in the same commit. Version numbers appear in exactly two places — `docs/stack.md` (the
inventory) and this file's prerequisites table — and both must agree with the project files. When a
document and the code disagree, the code wins: fix the document in the same change. The
[acceptance checklist in docs/README.md](docs/README.md#documentation-index) lists what a reviewer
should confirm.
