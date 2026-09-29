# Setup and configuration

From a clone to a running instance, plus the configuration reference. Commands are PowerShell; the
equivalents on other shells differ only in how environment variables are set.

Every command below was executed on Windows with .NET SDK 10.0.301 against commit `d9b1a42`
(the state this documentation starts from). Where something was **not** executed, it says so.

- [Prerequisites](#prerequisites)
- [Get the code and build](#get-the-code-and-build)
- [Run it: two processes (no container runtime needed)](#run-it-two-processes-no-container-runtime-needed)
- [Run it: orchestrated with Aspire](#run-it-orchestrated-with-aspire)
- [Verify the installation](#verify-the-installation)
- [Configuration reference](#configuration-reference)
- [Enabling accounts (Identity)](#enabling-accounts-identity)
- [Managing provider credentials](#managing-provider-credentials)
- [Styling: Tailwind v4 without Node](#styling-tailwind-v4-without-node)
- [Tests](#tests)
- [Troubleshooting](#troubleshooting)

## Prerequisites

| Requirement | Notes |
| --- | --- |
| .NET SDK 10 | Verified with 10.0.301. There is no `global.json`, so any 10.x SDK builds the solution. |
| A container runtime (optional) | Only for the Aspire path: Garnet and MongoDB run as containers. |
| Docker (optional) | Needed only if you enable accounts against a PostgreSQL instance you run yourself. |
| Node.js | **Not required.** See [Styling](#styling-tailwind-v4-without-node). |

If `dotnet` resolves to a runtime-only shim (a `dotnet` on `PATH` without an SDK), either call the SDK
explicitly:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build Veder.Server.slnx
```

or set both variables for the session, which is also required for `dotnet ef` because it re-resolves
`dotnet` from `PATH`:

```powershell
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'
$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
```

## Get the code and build

```powershell
git clone https://github.com/SudoScraTchY/Veder.Server.git
cd Veder.Server

dotnet build Veder.Server.slnx
```

Expected result: `Build succeeded`, **0 errors**, ~29 warnings (the visible ones are `NU1701` for the
legacy `Specification 1.0.1` package — see [stack.md](stack.md#known-constraints)). Verify the exit
code if you script this; a non-zero exit means something above is wrong, not a warning.

## Run it: two processes (no container runtime needed)

Two terminals. **Terminal 1 — the API:**

```powershell
dotnet run --project WebApi --launch-profile http
```

It listens on `http://localhost:5680` (the `http` profile in `WebApi/Properties/launchSettings.json`).
**Terminal 2 — the UI:**

```powershell
$env:ApiBaseUrl = 'http://localhost:5680'
dotnet run --project WebApp/WebApp --launch-profile http
```

It listens on `http://localhost:5690`. Open <http://localhost:5690/Tehran>.

`ApiBaseUrl` is what tells the UI where the API is when no orchestrator is publishing endpoints. If
you forget it, the UI tries service discovery, fails to resolve `api`, and the page shows *"Could not
load weather data"*. The startup log states which strategy was used:

```
Veder UI: aggregated API base address is http://localhost:5680 (ApiBaseUrl configuration override).
```

In the same folder, `dotnet run --project WebApp/WebApp` without `--launch-profile` uses the first
profile in the launch settings file, which is the same `http` profile.

**Do not** kill a running host before rebuilding: `dotnet build` fails with `MSB3021`/`MSB3027`
("file is locked by: WebApp/WebApi") when the previous run still holds the output DLLs. Stop the host
first.

## Run it: orchestrated with Aspire

```powershell
dotnet run --project AppHost
```

The AppHost starts Garnet (cache), MongoDB, the API and the UI, and injects their endpoints into each
process. The UI resolves the API through service discovery using the resource name `api`
(`https+http://api`), so **no port is configured anywhere**; the startup log then reads:

```
Veder UI: aggregated API base address is https+http://api (Aspire service discovery for the AppHost resource "api").
```

The Aspire dashboard prints a login URL on startup; open it to see resources, endpoints and traces.
Requires a running container runtime.

## Verify the installation

With the API running (`5680`):

```powershell
Invoke-WebRequest http://localhost:5680/health                       # 200, "Healthy"
Invoke-WebRequest http://localhost:5680/alive                        # 200
Invoke-WebRequest http://localhost:5680/openapi/v1.json              # 200, the OpenAPI document
Invoke-WebRequest http://localhost:5680/scalar/v1                    # 200, interactive API reference
Invoke-WebRequest http://localhost:5680/api/providers                # 200, providers with capabilities
Invoke-WebRequest 'http://localhost:5680/api/weather/aggregate?name=Tehran'   # 200, a real reading
```

Observed when this document was written (Development environment):

| Request | Result |
| --- | --- |
| `GET /health` | `200`, 7 bytes |
| `GET /alive` | `200`, 7 bytes |
| `GET /openapi/v1.json` | `200`, 10,328 bytes |
| `GET /scalar/v1` | `200`, 624 bytes |
| `GET /api/providers` | `200`, 1,427 bytes |
| `GET /api/weather/aggregate?name=Tehran` | `200`, 1,562 bytes |
| `GET http://localhost:5690/Tehran` (UI) | `200`, 15,945 bytes, with a daily outlook and a provenance chip naming `open-meteo` |

Note that `/health`, `/alive`, `/openapi/*` and `/scalar/*` are mapped **in Development only**.
In a non-Development environment the health endpoints are intentionally absent (see
`MapDefaultEndpoints`).

## Configuration reference

Values come from, in order of precedence: command-line arguments, environment variables,
user-secrets (Development), `appsettings.{Environment}.json`, `appsettings.json`. Under Aspire, the
AppHost injects connection strings and service endpoints into the child processes.

Environment-variable form replaces `:` with `__` (`OpenMeteo:ApiKey` → `OpenMeteo__ApiKey`).

### `OpenMeteo` section

| Key | Default | Meaning |
| --- | --- | --- |
| `OpenMeteo:ApiKey` | *(empty)* | Commercial API key. **The free tier needs none.** |
| `OpenMeteo:Enabled` | `true` | Registers the provider at all. |
| `OpenMeteo:Timezone` | `auto` | `auto`, or an IANA zone. |
| `OpenMeteo:TemperatureUnit` | `celsius` | `celsius` or `fahrenheit`. |
| `OpenMeteo:WindSpeedUnit` | `kmh` | `kmh`, `ms`, `mph`, `kn`. |
| `OpenMeteo:PrecipitationUnit` | `mm` | `mm` or `inch`. |
| `OpenMeteo:CellSelection` | `land` | `land` or `sea`. |
| `OpenMeteo:ForecastDays` | `7` | Forecast horizon. |
| `OpenMeteo:PastDays` | `0` | Days of history to request. |
| `OpenMeteo:TimeoutSeconds` | `20` | Per-attempt HTTP timeout. |
| `OpenMeteo:MaxRetryAttempts` | `3` | Retries on transient failures. |
| `OpenMeteo:MaxConcurrentRequests` | `4` | Per-process concurrency gate. |
| `OpenMeteo:Models` | *(empty)* | Optional model selection passed through to the API. |
| `OpenMeteo:RecordCalls` | `true` | Persist call records. |
| `OpenMeteo:ContactEmail` | *(empty)* | Sent as the contact parameter where the API expects it. |

`OpenMeteoOptions.Validate()` runs at startup and refuses to start on invalid values (for example an
unparseable timezone), which is why a bad value fails fast rather than at first request.

### `Aggregation` section

| Key | Default | Meaning |
| --- | --- | --- |
| `Aggregation:GeoCellDegrees` | `0.02` | Cache grid cell size in degrees. |
| `Aggregation:Units` | `metric` | Unit system for the aggregated response. |
| `Aggregation:Timezone` | `auto` | Timezone for daily grouping. |
| `Aggregation:CurrentVariables` | *(a curated list)* | Which current-condition fields to request. |

### `ProviderStore` section

| Key | Default | Meaning |
| --- | --- | --- |
| `ProviderStore:RootPath` | `%LOCALAPPDATA%\Veder\provider-store` | Where JSON records are written. Empty means the platform default. |
| `ProviderStore:MaxQueryCount` | `500` | Upper bound on records returned by a query. |

The store deliberately lives **outside the repository** so provider records can never be committed.
`tools/Veder.ProviderProbe` also accepts the `VEDER_PROVIDER_STORE` environment variable and maps it
to `ProviderStore:RootPath`; the API host uses the configuration key directly.

### Connection strings and the hosts

| Key | Effect when present | Effect when absent |
| --- | --- | --- |
| `ConnectionStrings:VederIdentity` | Accounts are enabled: the Identity store and endpoints are registered, migrations apply to PostgreSQL schema `identity`. | **The application still boots.** No identity endpoints are mapped; `/register` returns 404. |
| `ConnectionStrings:garnet` | `AddRedisDistributedCache` is used for `IDistributedCache`. | An in-memory distributed cache is used. |

### UI host

| Key | Meaning |
| --- | --- |
| `ApiBaseUrl` | Explicit API address for standalone runs. Absent → Aspire service discovery for the resource `api`. |
| `ASPNETCORE_ENVIRONMENT` | `Development` enables the WebAssembly debugger, the OpenAPI/Scalar endpoints and the health endpoints. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | When set, OpenTelemetry exports over OTLP. Absent → no exporter (the Aspire dashboard sets it). |

## Enabling accounts (Identity)

Accounts are optional and additive: no weather feature depends on them.

```powershell
# supply the connection string out of band — never in appsettings
dotnet user-secrets set "ConnectionStrings:VederIdentity" "<connection string>" --project WebApi

# apply the migrations (the DbContext lives in Infrastructure, the host is WebApi)
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'
$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
dotnet ef database update --project Infrastructure/Infrastructure.csproj --startup-project WebApi/WebApi.csproj
```

The migration list already contains `20260922193756_InitialIdentity`, which creates the seven
`AspNet*` tables plus `__identity_migrations` in schema `identity`. `Microsoft.EntityFrameworkCore.Design`
is referenced by **both** `Infrastructure` and `WebApi` because EF tooling requires it in the startup
project.

Password policy, as configured: minimum length 10, no non-alphanumeric requirement, unique email
required, no confirmed-account requirement.

The identity endpoints are the ASP.NET Core identity API: `POST /register`, `POST /login`,
`POST /logout` (mapped explicitly — the framework does not provide one), and `/manage/info` for the
signed-in user. Unauthenticated calls to `/api` receive **401/403**, not a redirect to a login page.

*Verification status:* the register → login → `/manage/info` → logout cycle and cross-process login
persistence were exercised against a local PostgreSQL container during development. The exact commands
above (including `dotnet ef`) were run in that environment; if your SDK resolves differently, set
`DOTNET_ROOT` as shown.

## Managing provider credentials

The provider probe manages credential lifecycle without a redeploy and without the UI:

```powershell
dotnet run --project tools/Veder.ProviderProbe -- credentials set      # store a key
dotnet run --project tools/Veder.ProviderProbe -- credentials status   # fingerprint and suffix only
dotnet run --project tools/Veder.ProviderProbe -- credentials rotate   # rotate
dotnet run --project tools/Veder.ProviderProbe -- credentials revoke   # revoke
dotnet run --project tools/Veder.ProviderProbe -- observations         # read recorded observations
```

Run with no arguments it exercises every provider capability in sequence and exits non-zero if any
capability fails — that makes it usable as a smoke test. Secrets are protected with DPAPI on Windows
(AES-256-GCM elsewhere) and only a fingerprint and suffix are ever readable.

The Open-Meteo free tier needs no key, so the probe's capability run works on a fresh clone.

## Styling: Tailwind v4 without Node

The committed `wwwroot/app.css` is the build output. To regenerate it you need the standalone CLI:

```powershell
# one-time, per machine; the binary is gitignored (~112 MB)
New-Item -ItemType Directory -Force tools/tailwind | Out-Null
Invoke-WebRequest https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-windows-x64.exe `
  -OutFile tools/tailwind/tailwindcss.exe

dotnet build WebApp/WebApp/WebApp.csproj   # BuildTailwind runs BeforeBuild
```

Input `wwwroot/app.tailwind.css`, output `wwwroot/app.css`. If the binary is missing, the build warns
and keeps the committed output rather than failing — so a fresh clone builds without the download.

## Tests

```powershell
dotnet test tests/Veder.Tests/veder.tests.csproj
```

Expected: **109 passed, 0 failed** (last run on this machine: 109 passed in ~900 ms). The suite needs
no database, no network and no containers.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| Host prints its startup banner, then dies; `SocketException (10013)` "access to a socket forbidden" | The port is inside a Windows **excluded** TCP range. It reads like a crash and is not one. | `netsh int ipv4 show excludedportrange protocol=tcp`, then use a port outside it. `tools/check-ports.ps1` automates the check. Full analysis: [runbook-startup-and-ports.md](runbook-startup-and-ports.md). |
| `MSB3021` / `MSB3027`: file is locked by `WebApp`/`WebApi` | A previous run is still hosting the output DLLs | Stop the running host, then rebuild |
| UI shows "Could not load weather data" | The UI cannot reach the API | Standalone: set `ApiBaseUrl`. Aspire: check the `api` resource is running |
| `/register` returns 404 | No `ConnectionStrings:VederIdentity` | Supply it (see above) — this is by design, not a failure |
| Both `/health` and `/scalar/v1` return 404 | Not running in `Development` | Set `ASPNETCORE_ENVIRONMENT=Development` |
| `dotnet ef` cannot find the SDK | `dotnet ef` re-resolves `dotnet` from `PATH` | Set `DOTNET_ROOT` and prepend it to `PATH` |
| Build fails on Linux/macOS with "project not found" | Solution references `Veder.Tests.csproj`; the tracked file is `veder.tests.csproj` | Reconcile the casing with `git mv` — see [decisions/0006](decisions/0006-portability-and-naming.md) |
| `NU1701` warnings on every build | Legacy `Specification 1.0.1` package | Expected; see [stack.md](stack.md#known-constraints) |

## See also

- [README](../README.md) — quickstart and layout
- [architecture.md](architecture.md) — how the pieces fit
- [stack.md](stack.md) — dependency inventory
- [providers/open-meteo.md](providers/open-meteo.md) — provider runbook
