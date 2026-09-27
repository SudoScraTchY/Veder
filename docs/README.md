# Veder — weather and air-quality service

A .NET 10 Aspire solution that aggregates weather and air-quality data from external providers behind
one cache-aware, failover-capable API, with an optional account layer and a Blazor UI.

Nothing in the product requires a sign-in. Accounts exist for future personalisation and are strictly
additive — no weather feature is gated behind them.

---

## 1. Layout

```
Veder.Server.slnx
├── AppHost/                 .NET Aspire orchestration (Garnet + MongoDB + api + webapp)
├── ServiceDefaults/         OpenTelemetry, health checks, standard resilience
├── Domain/                  Entities, value objects, capability interfaces, caching policy (pure)
├── Shared/                  Contracts shared by API and UI (queries, commands, responses)
├── UseCases/                Cortex.Mediator handlers (queries + commands)
├── Infrastructure/          Open-Meteo adapter, cache store, persistence, Identity store
│   ├── Providers/OpenMeteo/    wire layer: hosts, variable catalogue, request builder, parser, adapters
│   ├── Caching/               in-memory read-through cache
│   ├── Identity/              VederIdentityDbContext
│   └── Persistence/           JSON stores (call records, diagnostics, observations) + credential vault
├── WebApi/                  API host: weather + aggregated + provider endpoints, Identity endpoints
├── WebApp/                  Blazor Web App (Server host) styled with Tailwind v4
├── tools/Veder.ProviderProbe/  console probe that exercises every provider capability live
└── tests/Veder.Tests/       unit + in-process integration tests
```

**Toolchain note.** The `dotnet` on `PATH` on this machine is a runtime-only shim with no SDK. Always
use the absolute path: `& 'C:\Program Files\dotnet\dotnet.exe'`. The same applies to `dotnet ef`, which
re-resolves `dotnet` from `PATH` — set `DOTNET_ROOT=C:\Program Files\dotnet` and prepend it to `PATH`.

## 2. Running it

```powershell
# everything except the UI
& 'C:\Program Files\dotnet\dotnet.exe' build Veder.Server.slnx
& 'C:\Program Files\dotnet\dotnet.exe' test  Veder.Server.slnx

# API
& 'C:\Program Files\dotnet\dotnet.exe' run --project WebApi --urls http://127.0.0.1:5680

# UI (talks to the API over HTTP)
$env:ApiBaseUrl='http://127.0.0.1:5680'
& 'C:\Program Files\dotnet\dotnet.exe' run --project WebApp\WebApp --urls http://127.0.0.1:5690

# or the whole thing under Aspire
& 'C:\Program Files\dotnet\dotnet.exe' run --project AppHost\AppHost.csproj
```

Tailwind CSS is compiled as part of `dotnet build` (see §5). No Node.js is required or used.

## 3. Configuration

| Key | Purpose | Default |
|---|---|---|
| `OpenMeteo:ApiKey` | Optional commercial key, appended as `apikey`; masked in records and logs | *(none — free tier)* |
| `OpenMeteo:ForecastDays` | Forecast horizon | `7` |
| `OpenMeteo:PastDays` | Days of history prepended | `0` |
| `OpenMeteo:TimeoutSeconds` | HTTP timeout | `20` |
| `OpenMeteo:MaxConcurrentRequests` | Enforced by `ProviderConcurrencyGate` | `4` |
| `OpenMeteo:RecordCalls` | Persist raw payloads with each call record | `true` |
| `Aggregation:GeoCellDegrees` | Proximity bucket size | `0.02` (~2 km) |
| `Aggregation:Units` / `Timezone` | Cache-key shaping | `metric` / `auto` |
| `ProviderStore:RootPath` | Where call records, diagnostics, observations and the credential vault live | `%LOCALAPPDATA%\Veder\provider-store` |
| `ConnectionStrings:VederIdentity` | Postgres connection for Identity. **Absent → Identity is skipped entirely** and the app still boots | *(none)* |
| `ApiBaseUrl` | Where the Blazor UI looks for the API | `http://localhost:5680` |

Environment variables work in the usual ASP.NET Core form (`ConnectionStrings__VederIdentity`,
`OpenMeteo__ApiKey`). `OpenMeteoOptions.Validate()` runs at startup and refuses to start on an invalid
value, naming the key and its accepted range.

## 4. Cache strategy

Two properties matter: **nearby requests share an entry**, and **fresh-looking data expires quickly**.

**Keying.** `v{schema}|{dataType}|{providerId}|{geoBucket}|{units}|{variables}|{models}|{timezone}`,
composed with `InvariantCulture` (`GeoBucketer`, `WeatherCacheKey`).

- Proximity is **floor-to-grid cells plus an eight-cell neighbourhood**, not rounding. Rounding puts two
  points two metres apart on opposite sides of a cell edge, produces `-0.000` for small negative
  latitudes, breaks at the antimeridian, and is meaningless at the poles. Longitude wraps onto a single
  seam at ±180 and collapses above ±75°.
- Everything that changes the response is in the key. Units, requested variables, model selection and
  timezone all alter the payload, so a key that omitted them would serve 22 °C as 22 °F. The schema
  version lets a shape change invalidate old entries cleanly.
- Culture matters: under `fa-IR` or `de-DE`, default number formatting renders `35,700,51,400`, and that
  comma collides with the key separator.

**Lifetimes** (`CacheTtlPolicy`): current conditions 7 min, air quality 45 min, forecast and marine 4 h,
archive 24 h, elevation and geocoding 30 d — each jittered ±10% so a hot entry cannot be refreshed by
every caller at the same instant.

**Read path** (`IWeatherCacheStore`): a hit is served; concurrent readers of one expiring key collapse
into **one** upstream call (single-flight, verified: 200 readers → 1 call, 199 coalesced); if the provider
fails inside the grace window the previous value is served marked `IsDegraded` with its **true** source.

**Telemetry is counters, not rows.** Recording every hit would put 9.5k writes/sec on the request path at
10k rps with a 95% hit rate. Counters plus full-fidelity events for miss, eviction, failure and fallback.

## 5. Failure handling and provider selection

`ProviderFailurePolicy` classifies every failure and decides the consequences:

| Class | Examples | Fails over? | Affects provider health? |
|---|---|---|---|
| `Availability` / `Timeout` / `MalformedResponse` | 5xx, connection reset, deadline, unparseable body | yes | yes |
| `RateLimit` | HTTP 429 | yes | yes |
| `Rejection` | 400/422, unknown variable, out-of-range date | **no** | **no** |
| `Authentication` | 401/403 | no | no |

The narrow health rule is deliberate: one inland marine query answering "no coverage" must not open a
breaker and degrade weather for every user. The narrow failover rule is equally deliberate: a wrong
request stays wrong whoever you ask.

**Naming a provider is a preference, not a guarantee.** `?providerId=…` puts that provider first and
falls back only on availability failures; `&strictProvider=true` returns the failure instead of
substituting, for callers comparing providers.

**A substitute is never cached under the requested provider's key.** Since `providerId` is part of the
key, doing so would permanently mislabel a foreign payload — every later read, strict caller and quota
count would trust it.

**Resilience pipeline** (per named `HttpClient`): retry with exponential backoff and jitter honouring
`Retry-After` (3 attempts), attempt timeout 15 s, total 60 s, circuit breaker with a 30 s sampling window.

## 6. Endpoints

| Route | Purpose |
|---|---|
| `GET /api/weather/aggregate?lat&lon \| ?name&providerId&strictProvider&includeAirQuality` | **The one the UI uses**: location, current conditions, daily outlook, air quality, provenance |
| `GET /api/weather/forecast?lat&lon&providerId` | Current conditions |
| `GET /api/weather/aqi?lat&lon&providerId` | Air quality |
| `GET /api/weather/series?lat&lon` | Full hourly/daily series |
| `GET /api/weather/archive?lat&lon&start&end` | Reanalysis archive |
| `GET /api/weather/marine?lat&lon` | Waves, swell, sea-surface temperature |
| `GET /api/weather/elevation?lat&lon` | Ground elevation |
| `GET /api/geocoding/search?name&count&language` | Place-name lookup |
| `GET /api/providers` | Registry view with capability flags |
| `POST /register`, `POST /login`, `POST /logout`, `GET /manage/info`, … | Identity, entirely optional |

Errors are RFC 9110 problem details (`InvalidCoordinates`, `ProviderRejectedRequest`,
`AllProvidersUnavailable`, …). Anonymous requests to `/api` receive **401**, never a redirect to a login
page.

## 7. Identity (optional)

`VederIdentityDbContext` stores users in the `identity` schema with its own migration history. Registration
happens only when `ConnectionStrings:VederIdentity` is set; otherwise `AddVederIdentity` returns `false`
and the host maps no Identity endpoints.

The framework's `MapIdentityApi` provides register, login, refresh, confirm and manage — but **not
logout**, which is why `POST /logout` is written explicitly in `WebApi\Identity\IdentityRegistration.cs`.

```powershell
# migrations (DbContext lives in Infrastructure, host is WebApi)
$env:DOTNET_ROOT='C:\Program Files\dotnet'; $env:PATH="C:\Program Files\dotnet;$env:PATH"
$env:ConnectionStrings__VederIdentity='Host=127.0.0.1;Port=5432;Database=veder;Username=…;Password=…'
& 'C:\Program Files\dotnet\dotnet.exe' ef migrations add InitialIdentity --project Infrastructure --startup-project WebApi --context VederIdentityDbContext --output-dir Identity/Migrations
& 'C:\Program Files\dotnet\dotnet.exe' ef database update --project Infrastructure --startup-project WebApi --context VederIdentityDbContext
```

No 10.x SQLite provider exists in the local package cache, so Postgres is the store of choice: it is
version-compatible with the EF Core already referenced, whereas pairing a 9.x provider with EF Core 10 is
the classic version-skew trap.

## 8. Front end

Blazor Web App (`WebApp` host + `WebApp.Client`), styled **exclusively** with Tailwind CSS v4. Bootstrap
has been removed from `App.razor`.

- `wwwroot\app.tailwind.css` — input with design tokens (`@theme`) and two components.
- `wwwroot\app.css` — compiled output, regenerated on every build.
- `WebApp.csproj` — the `BuildTailwind` target runs before build; if the CLI is missing it warns and uses
  the committed CSS, so a fresh clone still builds.

**Design direction — "cartographic warm data".** Parchment ground, warm ink for type, ochre for emphasis,
sage for data, terracotta reserved for degradation and provider switching. The `.provenance-chip`
component gives the product its visual grammar: every reading states which provider produced it and how
the read was satisfied.

**Persian layer (optional, non-religious).** Vazirmatn is the Persian text face; direction is a single
`dir` attribute on the page wrapper driven by the language toggle, so the same markup serves both scripts
instead of duplicating it. No religious motifs are used.

Tailwind is built by the **standalone CLI** (v4.3.3) because this machine has no Node.js:

```powershell
# one-time, per machine; the binary is gitignored
Invoke-WebRequest https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-windows-x64.exe -OutFile tools\tailwind\tailwindcss.exe
```

## 9. Runbook

```powershell
# exercise every provider capability against the live API through the product's own code path
& 'C:\Program Files\dotnet\dotnet.exe' run --project tools\Veder.ProviderProbe
$env:VEDER_PROVIDER_STORE="$env:TEMP\veder-probe"; & 'C:\Program Files\dotnet\dotnet.exe' run --project tools\Veder.ProviderProbe
```

Stored state (default `%LOCALAPPDATA%\Veder\provider-store`):

| File | Contents |
|---|---|
| `provider-calls.jsonl` | append-only call records: operation, sanitized URL, outcome, status, duration, bytes, points |
| `provider-diagnostics.json` | last success / last failure per provider, consecutive-failure count |
| `observations.json` | modelled readings, keyed by a deterministic id so replays upsert |
| `credentials/` | vault: encrypted secret + fingerprint/hint metadata (never the plaintext) |
| `manifest.json` | schema version for the whole store |

```powershell
Get-Content "$env:LOCALAPPDATA\Veder\provider-store\provider-calls.jsonl" -Tail 5 |
  ForEach-Object { $_ | ConvertFrom-Json } |
  Format-Table operation, outcome, httpStatus, durationMs, pointCount, sanitizedUrl
```

| Symptom | Likely cause | Action |
|---|---|---|
| `ProviderAuthenticationException` | key refused | re-check `OpenMeteo__ApiKey`; the free tier needs none — unset it |
| `AllProvidersUnavailable` (503) | every candidate failed | inspect diagnostics; check connectivity |
| `InvalidCoordinates` (400) | latitude/longitude out of range | fix the request |
| `ProviderRejectedRequest` (400) | bad variable, bad date range, archive inside the 5-day lag | read the `detail` field |
| `ProviderRateLimitedException` | burst over the free-tier budget | lower `MaxConcurrentRequests` or add a key |
| Identity endpoints 404 | no `ConnectionStrings:VederIdentity` → Identity intentionally skipped | set the connection string |

**Rollback**

| Goal | Action |
|---|---|
| Disable accounts | unset `ConnectionStrings:VederIdentity`. The host maps no Identity endpoints; everyone stays anonymous. |
| Disable the provider | stop calling `AddOpenMeteoProvider`, or set `OpenMeteo:Enabled=false` |
| Drop to the free tier | clear `OpenMeteo:ApiKey` |
| Remove the provider entirely | delete `Infrastructure/Providers/OpenMeteo/**` and its DI call — everything else reaches it only through domain interfaces |
| Purge stored data | delete `%LOCALAPPDATA%\Veder\provider-store` |

## 10. Tests

`& 'C:\Program Files\dotnet\dotnet.exe' test Veder.Server.slnx` — 109 tests covering cache key derivation
(culture invariance, the antimeridian, negative zero, polar collapse, unit/model/variable/timezone
separation), TTL policy and jitter, read-through behaviour (single-flight, stale-on-failure, counters),
failure classification and failover rules, Open-Meteo request validation and parsing, provider mapping,
the aggregated composer, the credential vault, observation idempotency, and in-process HTTP endpoint
tests that boot the real pipeline with a stubbed transport.

---

## 11. Startup and ports

The API and UI ports live in the launch profiles and are **not** free choices: Windows reserves
whole ranges for Hyper-V, WSL and Docker, and a socket inside one of them fails with
`SocketException (10013)`, which Kestrel treats as fatal. Every port this project used before
(`5199`, `5210`, `5270`, `5273`) sat inside a reserved range, which is why the API appeared to start
and then die, and why the UI could never reach it.

Current: API `5680` (http) / `5681` (https), UI `5690` (http) / `5691` (https).

Check the machine before blaming the code:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-ports.ps1
```

Full diagnosis, the change list, and how to confirm the fix:
[`runbook-startup-and-ports.md`](runbook-startup-and-ports.md).