# Open-Meteo provider — capability matrix and operator guide

Provider id: **`open-meteo`** · Display name: **Open-Meteo** · Implemented in `Infrastructure/Providers/OpenMeteo`.
First external provider integrated into Veder; every other provider in the registry is untouched by its code paths.

---

## 1. Capability coverage matrix

Every documented Open-Meteo dataset, plus the request features that apply across datasets. "Test that proves it"
names the automated test or the end-to-end probe step that exercises the path.

| # | Capability / endpoint | Status | Code path | Proof |
|---|---|---|---|---|
| 1 | Forecast API `/v1/forecast` — `current` block | Implemented | `OpenMeteoVariables.Current` → `OpenMeteoRequestBuilder.Build` → `OpenMeteoWeatherProvider.GetForecastAsync` → `OpenMeteoMapper.ToWeatherForecast` | `OpenMeteoRequestBuilderTests.Build_RendersUnixTimeAndExpectedGroups`, `OpenMeteoMapperTests.ToWeatherForecast_UsesTheCurrentObservation`, probe "weather forecast" |
| 2 | Forecast `minutely_15` block (15-minute variables: radiation, CAPE, visibility, lightning…) | Implemented (catalogue + transport + parser; no dedicated adapter method yet) | `OpenMeteoVariables.Minutely15`, parser zips any requested array | `OpenMeteoParserTests.Parse_ZipsParallelArraysAndMergesUnits` |
| 3 | Forecast `hourly` block (61 variables) | Implemented | `OpenMeteoVariables.Hourly`, `OpenMeteoWeatherProvider.HourlyVariables` | probe "weather forecast series" (50 points), parser tests |
| 4 | Forecast `daily` block (24 variables) | Implemented | `OpenMeteoVariables.Daily`, `OpenMeteoWeatherProvider.DailyVariables` | probe "weather forecast series", parser tests |
| 5 | Forecast `past_days` (0–92) and `forecast_days` (1–16) | Implemented with validation | `OpenMeteoOptions.PastDays/ForecastDays`, `OpenMeteoRequestBuilder` clamps and rejects out-of-range | `OpenMeteoOptions.Validate`, builder tests |
| 6 | Model selection (`models=`) — 23 deterministic models | Implemented | `OpenMeteoVariables.ForecastModels`, `OpenMeteoOptions.Models` / `OpenMeteoRequest.Models` | `OpenMeteoVariables` catalogue; URL rendered by builder |
| 7 | Timezone handling (`timezone=auto\|UTC\|IANA`) and `utc_offset_seconds` | Implemented | `OpenMeteoRequestBuilder` (`timezone=`), parser reads `timezone`, `utc_offset_seconds` | `OpenMeteoParserTests.Parse_ZipsParallelArraysAndMergesUnits` |
| 8 | `timeformat=unixtime` (unambiguous absolute timestamps) | Implemented (always requested) | `OpenMeteoRequestBuilder.Build`; `OpenMeteoResponseParser.ReadTime` also accepts ISO | builder + parser tests |
| 9 | Unit selection (`temperature_unit`, `wind_speed_unit`, `precipitation_unit`) | Implemented | `OpenMeteoOptions.TemperatureUnit/WindSpeedUnit/PrecipitationUnit` | `OpenMeteoRequestBuilderTests` |
| 10 | `cell_selection` (`land`/`sea`/`nearest`) | Implemented | `OpenMeteoOptions.CellSelection`, `OpenMeteoRequest.CellSelection` | builder tests |
| 11 | Elevation returned with each response (`elevation`) | Implemented | `OpenMeteoResponseParser` → `WeatherSeries.ElevationMeters` | parser tests (38.0 m) |
| 12 | Air Quality API `/v1/air-quality` — current (AQI EU/US, pollutants, dust, UV, 6 pollen types) | Implemented | `OpenMeteoVariables.AirQuality` → `OpenMeteoAirQualityProvider.GetCurrentAsync` → `OpenMeteoMapper.ToAirQualityReading` | `OpenMeteoMapperTests.ToAirQualityReading_PrefersEuropeanAqiAndKeepsEveryPollutant`, probe (17 pollutants) |
| 13 | Air Quality hourly series | Implemented | `OpenMeteoAirQualityProvider.GetAirQualitySeriesAsync` | probe "air quality series" (48 points) |
| 14 | Historical Weather API `/v1/archive` (ERA5, from 1940) | Implemented | `OpenMeteoDataset.Archive` → `OpenMeteoExtendedProvider.GetArchiveAsync` | probe "historical archive" (175 points), builder range/validation tests |
| 15 | Historical Forecast API (past forecasts since 2022) | Implemented | `OpenMeteoDataset.HistoricalForecast` → `GetHistoricalForecastAsync` | probe "historical forecast" (125 points) |
| 16 | Marine Weather API (waves, swell, currents, SST) | Implemented | `OpenMeteoDataset.Marine` → `GetMarineAsync` | probe "marine" (wave height 3.10 m) |
| 17 | Ensemble API (31 members) | Implemented | `OpenMeteoDataset.Ensemble` → `GetEnsembleAsync` | probe "ensemble" (31 temperature members) |
| 18 | Climate API (CMIP6, 7 models, 1950–2050) | Implemented | `OpenMeteoDataset.Climate` → `GetClimateProjectionAsync` | probe "climate projection" (5 daily points), `OpenMeteoVariables.ClimateModels` |
| 19 | Flood API (GloFAS river discharge incl. percentiles) | Implemented | `OpenMeteoDataset.Flood` → `GetFloodAsync` | probe "flood" (0.57 m³/s) |
| 20 | Elevation API `/v1/elevation` (up to 100 coordinates per call) | Implemented | `OpenMeteoClient.GetElevationsAsync` → `OpenMeteoExtendedProvider.GetElevationsAsync` | probe "elevation lookup" (Berlin 38 m, Hamburg 13 m); 100-coordinate cap enforced |
| 21 | Geocoding search `/v1/search` (name, count 1–100, language) | Implemented | `OpenMeteoClient.SearchLocationsAsync` → parser → `GetGeocodingProvider.SearchAsync` | probe "geocoding search" (3 matches) |
| 22 | Geocoding lookup `/v1/get?id=` | Implemented | `OpenMeteoClient.GetLocationsByIdAsync` → `GetByIdAsync` / `GetByIdsAsync` | probe "geocoding by id" (Berlin, Germany, 74 m) |
| 23 | Commercial API key (`apikey` query parameter) | Implemented, never logged | `OpenMeteoOptions.ApiKey`, `OpenMeteoRequestBuilder` (masked `SanitizedUrl`) | `OpenMeteoRequestBuilderTests.Build_MasksTheCredentialInTheRecordedUrl` |
| 24 | Typed error mapping (400/401/403/429/5xx/timeout/transport/malformed) | Implemented | `OpenMeteoClient.SendAsync` | `OpenMeteoClientFailureTests` (4 cases) |
| 25 | Retries with exponential backoff + jitter, honouring `Retry-After` | Implemented | `AddOpenMeteoProvider` resilience handler (3 attempts, 400 ms base, jitter, 429 included) | `OpenMeteoClientFailureTests.TooManyRequests_BecomesARateLimitExceptionHonouringRetryAfter`, config in `OpenMeteoServiceCollectionExtensions` |
| 26 | Timeouts (attempt 15 s, total 60 s, HTTP client 20 s default) | Implemented | resilience handler + `OpenMeteoOptions.TimeoutSeconds` | builder/options validation |
| 27 | Circuit breaker (30 s sampling window) | Implemented | resilience handler | configuration in DI extension |
| 28 | Durable call records with sanitized URLs, outcomes, byte counts, point counts | Implemented | `JsonProviderCallRecordStore`, `ProviderCallRecorder` | `ProviderStoreTests.AppendAndQuery_RoundTripsRecordsNewestFirst`, probe "Persisted evidence" (23 records) |
| 29 | Last-success / last-failure diagnostics with consecutive-failure counter | Implemented | `JsonProviderDiagnosticsStore` | `ProviderStoreTests.Diagnostics_TrackTheLastSuccessAndFailureAndConsecutiveFailures` |
| 30 | Store schema versioning + migration hook | Implemented | `ProviderStoreManifest` (version 1) | `ProviderStoreTests.Manifest_IsWrittenAndRejectedWhenNewerThanTheBuild` |
| 31 | Metrics + tracing | Implemented | `OpenMeteoTelemetry` (`veder.openmeteo.*`, `Veder.OpenMeteo` activity source) | instrument list below; emitted during probe run |
| 32 | Structured logs for every non-success | Implemented | `OpenMeteoClient` (Information/Warning/Error with sanitized URL) | probe output shows the mapped 404 warning from run 1 |

### Not supported — and why

| Capability | Status | Reason |
|---|---|---|
| Geocoding bulk id list (`/v1/get-by-id`) | Unsupported | The documented path returns **HTTP 404** for every parameter shape tried (`?id=`, `?ids=`); the working lookup is `/v1/get?id=` (single id), which is what we call. Verified live on 2026-09-21. |
| Webhooks / push callbacks | Unsupported | Open-Meteo is a pull-only API; it publishes no webhook or subscription mechanism, so there is nothing to verify or make idempotent. |
| Pagination | Unsupported | Endpoints return one complete payload per request (`/v1/elevation` batches up to 100 coordinates). There is no page/cursor concept to follow. |
| Streaming responses | Unsupported | Responses are single JSON documents; there is no streaming/chunked event API. |
| Commercial-only "customer" endpoints | Out of scope | Only reachable with a paid subscription; this task is limited to the public/free surface plus the `apikey` parameter. |
| `Alerts` capability flag | Not claimed | Open-Meteo has no severe-weather alert dataset; the flag stays unset for this provider rather than being mapped onto something it does not provide. |

---

## 2. Configuration

Bound from the `OpenMeteo` configuration section: `appsettings.json`, environment variables
(`OpenMeteo__ApiKey`), or `dotnet user-secrets`. Defaults are safe for the free tier.

| Key | Default | Meaning | Validation |
|---|---|---|---|
| `OpenMeteo:ApiKey` | *(empty)* | Commercial key, appended as `apikey`. Masked in records and logs. | — |
| `OpenMeteo:Enabled` | `true` | Master switch for the provider. | — |
| `OpenMeteo:Timezone` | `auto` | `auto`, `UTC`, or an IANA name. | non-empty |
| `OpenMeteo:TemperatureUnit` | `celsius` | API unit parameter. | — |
| `OpenMeteo:WindSpeedUnit` | `kmh` | `kmh`, `ms`, `mph`, `kn`. | — |
| `OpenMeteo:PrecipitationUnit` | `mm` | `mm` or `inch`. | — |
| `OpenMeteo:CellSelection` | `land` | `land`, `sea`, `nearest`. | non-empty |
| `OpenMeteo:ForecastDays` | `7` | Forecast horizon. | 1–16 |
| `OpenMeteo:PastDays` | `0` | Days of history prepended. | 0–92 |
| `OpenMeteo:TimeoutSeconds` | `20` | HTTP client timeout. | 1–300 |
| `OpenMeteo:MaxRetryAttempts` | `3` | Documented retry budget. | 0–10 |
| `OpenMeteo:MaxConcurrentRequests` | `4` | Documented concurrency budget. | 1–32 |
| `OpenMeteo:Models` | *(empty)* | Default model override (e.g. `icon_seamless`). | — |
| `OpenMeteo:RecordCalls` | `true` | Persist the raw payload with each record. | — |
| `OpenMeteo:ContactEmail` | *(empty)* | Sent as `From` for heavier use, as Open-Meteo requests. | must contain `@` |
| `ProviderStore:RootPath` | `%LOCALAPPDATA%\Veder\provider-store` | Where records live. **Never inside the repository.** | — |

Invalid configuration fails fast: the options validator runs `OpenMeteoOptions.Validate()` at startup and the
host refuses to start with a message naming the offending key and its accepted range.

### Credentials: add, rotate, revoke

The free tier needs no credential at all. When a commercial key is required, store it in the vault — it is
encrypted at rest (Windows DPAPI, AES-256-GCM elsewhere) and only its fingerprint and hint are kept in clear text:

```powershell
# add / rotate / revoke / inspect — no rebuild, no redeploy; the next start picks the vault up
& 'C:\Program Files\dotnet\dotnet.exe' run --project tools\Veder.ProviderProbe -- credentials set <secret>
& 'C:\Program Files\dotnet\dotnet.exe' run --project tools\Veder.ProviderProbe -- credentials rotate <secret>
& 'C:\Program Files\dotnet\dotnet.exe' run --project tools\Veder.ProviderProbe -- credentials revoke
& 'C:\Program Files\dotnet\dotnet.exe' run --project tools\Veder.ProviderProbe -- credentials status
```

Verified on 2026-09-22 (real run): `set` → fingerprint `25af245ded5c`, hint `…1234`, `protection=dpapi`; `rotate` →
fingerprint `a58fb18c4dde`, hint `…9876`, `rotations=1` with the original `created` timestamp preserved; `revoke` →
ciphertext zeroed (`open-meteo.cred` = 0 bytes) and metadata marked `revoked=True`; a repository-wide and
vault-wide text search for either secret returned **no plaintext on disk**.

Resolution order is vault → configuration (`OpenMeteo__ApiKey`, or `dotnet user-secrets set "OpenMeteo:ApiKey" "<key>"`,
`UserSecretsId` is already set on `AppHost.csproj`) → free tier. The key is never written to source, never included
in a call record (`SanitizedUrl` replaces it with `***`) and never logged.

---

## 3. Persistence

| Artifact | Path (default) | Format |
|---|---|---|
| Manifest | `<root>/manifest.json` | `{ schemaVersion, createdAt, updatedAt }` |
| Call records | `<root>/provider-calls.jsonl` | one JSON object per line, append-only |
| Diagnostics | `<root>/provider-diagnostics.json` | single document, rewritten atomically (temp + move) |
| Modelled observations | `<root>/observations.json` | keyed by a deterministic id, upserted atomically |
| Credential vault | `<root>/credentials/` | DPAPI/AES-GCM ciphertext + metadata (fingerprint, hint, timestamps) |

Observations are the modelled view of what we synced (`weather` / `air-quality` with temperature, humidity, AQI,
dominant pollutant and a pollutant count). Their id is `provider|kind|bucket|observedAt`, so replaying the same
reading **updates in place instead of duplicating** — verified live: two probe runs produced 4 rows across 2 distinct
instants, not 8.

Call records carry `id`, `providerId`, `operation`, `sanitizedUrl`, `startedAt`, `durationMs`, `outcome`
(`success` / `request-rejected` / `authentication-failed` / `rate-limited` / `provider-unavailable` / `timeout`),
`httpStatus`, `errorCode`, `errorMessage`, `responseBytes`, `pointCount` and the raw payload when
`RecordCalls` is on. That answers "what did we send, what came back" after the fact.

```powershell
# inspect the newest records
Get-Content "$env:LOCALAPPDATA\Veder\provider-store\provider-calls.jsonl" -Tail 5 |
  ForEach-Object { $_ | ConvertFrom-Json } |
  Format-Table operation, outcome, httpStatus, durationMs, pointCount, sanitizedUrl

# last success / last failure
Get-Content "$env:LOCALAPPDATA\Veder\provider-store\provider-diagnostics.json" -Raw | ConvertFrom-Json |
  Format-List
```

Schema changes are versioned through `ProviderStoreManifest`: an older layout is upgraded in place on first
write, and a store written by a *newer* build is refused with an explanatory error instead of being corrupted.

---

## 4. Observability

Metrics (meter `veder.openmeteo`, all tagged with `operation`):

| Instrument | Type | Meaning |
|---|---|---|
| `veder.openmeteo.request.count` | counter | calls attempted |
| `veder.openmeteo.request.failures` | counter | calls that ended in an error (tag `error.type`) |
| `veder.openmeteo.request.rate_limited` | counter | HTTP 429 responses |
| `veder.openmeteo.request.retries` | counter | transport retries observed |
| `veder.openmeteo.response.bytes` | counter | payload bytes received |
| `veder.openmeteo.request.duration` | histogram (ms) | wall-clock duration |

Tracing: activity source `Veder.OpenMeteo` with `openmeteo.operation`, `openmeteo.url` (sanitized) and
`openmeteo.status` tags. Logs: `Information` on recorded failures, `Warning` on 4xx/5xx/timeout with the
sanitized URL, `Error` on authentication rejection.

---

## 5. Runbook

### Exercise every capability end to end

```powershell
# from Veder.Server — makes 13 real calls against the public API through the product's own code path
& 'C:\Program Files\dotnet\dotnet.exe' run --project tools\Veder.ProviderProbe

# keep the evidence somewhere disposable
$env:VEDER_PROVIDER_STORE = "$env:TEMP\veder-probe"; & 'C:\Program Files\dotnet\dotnet.exe' run --project tools\Veder.ProviderProbe
```

Expected: 13 `[ok]` lines, a "Persisted evidence" block, `RESULT: all Open-Meteo capabilities responded
successfully.`, exit code 0. Re-running is safe and reproduces the same capability set; each run appends new
records rather than rewriting history (verified: 23 records after two runs).

### Failure signatures

| Symptom | Likely cause | Action |
|---|---|---|
| `ProviderAuthenticationException` | key rejected (401/403) | re-check `OpenMeteo__ApiKey`; free tier needs none — unset it |
| `ProviderRequestException: unknown variable …` | requested a variable outside the dataset catalogue | use a name from `OpenMeteoVariables` |
| `ProviderRequestException: … requires both StartDate and EndDate` | archive/climate call without a range | supply both dates |
| `ProviderRequestException: … beyond the latest available date` | archive end date inside the 5-day reanalysis lag | move the end date back |
| `ProviderRateLimitedException` (has `RetryAfter`) | burst exceeded the free-tier minute/hour budget | lower `MaxConcurrentRequests`, back off, or buy a commercial key |
| `ProviderUnavailableException` | 5xx, DNS, TLS or timeout | check connectivity; the resilience handler already retried |
| 404 on a geocoding id | used `/v1/get-by-id` | use `/v1/get?id=` (already handled in code) |

### Disable or roll back

1. **Disable at runtime:** stop calling `AddOpenMeteoProvider`, or set `OpenMeteo:Enabled=false` (the options
   validator then skips the forecast-horizon check). The provider is additive: nothing else references it, so the
   rest of the solution keeps working.
2. **Remove the credential:** clear `OpenMeteo__ApiKey` and restart — the provider drops back to the free tier.
3. **Remove the code:** delete `Infrastructure/Providers/OpenMeteo/**`, the DI extension
   `Infrastructure/Helpers/DI/OpenMeteoServiceCollectionExtensions.cs`, the probe under
   `tools/Veder.ProviderProbe`, and the `OpenMeteo`/provider-store registrations in `WebApi/Program.cs`.
   Because every other project reaches the provider only through the domain interfaces, removing it cannot
   break the registry, the selector or the existing endpoints.
4. **Purge stored data:** delete `%LOCALAPPDATA%\Veder\provider-store`. Nothing else reads that directory.

---

## 6. HTTP surface

`WebApi` exposes the provider through the product's own endpoints. The first two go through the mediator and the
existing provider selector (registry → priority → cache → adapter), so they exercise the same path a future
provider would; the rest expose the extended datasets directly.

| Route | Capability | Verified |
|---|---|---|
| `GET /api/providers` | registry view, incl. the 10 capability flags Open-Meteo advertises | 200 |
| `GET /api/weather/forecast?lat&lon&providerId?` | current conditions via `IProviderSelector` | 200 · `10.2 °C`, `Clear sky`, humidity `83`, wind `10.2 km/h` from `302°` |
| `GET /api/weather/aqi?lat&lon&providerId?` | air quality via `IProviderSelector` | 200 · AQI `18 (Good)`, pollutants incl. `pm2_5 4.6` |
| `GET /api/weather/series?lat&lon` | full hourly/daily series (`IWeatherSeriesProvider`) | 200 · `tz=Europe/Berlin`, elevation `38 m` |
| `GET /api/geocoding/search?name&count&language` | place lookup | 200 · Berlin `52.52437,13.41053`, population `3426354` |
| `GET /api/weather/elevation?lat&lon` | elevation lookup | 200 · `38 m` |
| `GET /api/weather/archive?lat&lon&start&end` | reanalysis archive | 200 |
| `GET /api/weather/marine?lat&lon` | wave/swell/sea-surface | 200 |
| `GET /api/weather/forecast?lat=999` | invalid input | 400 `InvalidCoordinates` (RFC 9110 problem details) |
| `GET /api/weather/archive` inside the reanalysis lag | provider request rejection | 400 `ProviderRejectedRequest` with the exact reason |

All ten responses above were captured from a live run against the public API on 2026-09-22
(`http://127.0.0.1:5199`, `ASPNETCORE_ENVIRONMENT=Development`). The routing failure that first appeared here —
`Duplicate endpoint name 'GetWeatherForecast'` colliding with the template controller — is fixed; endpoint names
are now capability-scoped, and `WeatherEndpointTests` locks the behaviour in.

---

## 7. Known limitations

- **Adapters are one class per concern, not per dataset.** `OpenMeteoExtendedProvider` implements seven
  capability interfaces over one client; splitting them later is mechanical and does not change the wire layer.
- **Minutely-15 has no dedicated adapter method.** The catalogue, request builder and parser all support the
  grouping, but no `IMinutelyWeatherProvider` capability interface exists in the domain yet, so callers would
  have to use `OpenMeteoClient` directly. Documented rather than invented as a domain interface.
- **Diagnostics keep counters and the last events, not a full history.** Complete history lives in the call
  records; the diagnostics document is a fast "what happened last" view.
- **The observation store rewrites one document per upsert.** Correct and idempotent at this scale; a store with
  millions of observations would want an append-only or columnar layout, which is the Mongo/dedicated store step.
- **`MaxConcurrentRequests` is enforced per process.** `ProviderConcurrencyGate` caps in-flight calls for one
  instance; a multi-instance deployment would need a distributed limiter.
