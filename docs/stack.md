# Technology stack

Every language, framework, runtime and significant dependency in this repository, with the version
it is actually pinned to, what it does here, and why it was chosen. Versions were read from the
project files, not from memory — the command to re-read them is at the end.

- [Runtime and language](#runtime-and-language)
- [Orchestration: .NET Aspire](#orchestration-net-aspire)
- [Web: API host](#web-api-host)
- [Web: user interface](#web-user-interface)
- [Data and persistence](#data-and-persistence)
- [Application plumbing](#application-plumbing)
- [Observability](#observability)
- [Credentials and cryptography](#credentials-and-cryptography)
- [Testing](#testing)
- [Operator tooling](#operator-tooling)
- [Front-end build tooling](#front-end-build-tooling)
- [Known constraints](#known-constraints)
- [Refreshing this inventory](#refreshing-this-inventory)

## Runtime and language

| Item | Version | Notes |
| --- | --- | --- |
| .NET SDK | 10.0.301 (verified) | All nine projects target `net10.0`. No `global.json`, so any 10.x SDK builds it. |
| .NET runtime | 10.0.9 (verified) | Ships with the SDK. |
| C# | SDK default for `net10.0` | No `LangVersion` override anywhere. |
| OS used for development | Windows 10 (10.0.19045) | Windows-specific behaviour matters in two places: the port exclusions and DPAPI. |

There is exactly one target framework in the solution, which removes the multi-targeting questions
that usually appear in a stack document.

## Orchestration: .NET Aspire

| Package | Version | Projects | Role and reason |
| --- | --- | --- | --- |
| `Aspire.AppHost.Sdk` | 13.4.6 | `AppHost` | Declares the distributed application. Chosen because the alternative — three terminals and a documented order of operations — is the failure mode this repository keeps hitting. |
| `Aspire.Hosting.Garnet` | 13.4.6 | `AppHost` | Provision the cache resource. Garnet is Redis-compatible, so the API talks RESP either way. |
| `Aspire.Hosting.MongoDB` | 13.4.6 | `AppHost` | Declares MongoDB with a data volume and Mongo Express. **Currently provisioned but not consumed by any application project** — see [decisions/0004](decisions/0004-storage-choices.md). |
| `Aspire.StackExchange.Redis` | 13.4.6 | `WebApi` | Redis client used for the distributed cache path. |
| `Aspire.StackExchange.Redis.DistributedCaching` | 13.4.6 | `WebApi` | `AddRedisDistributedCache`; selected only when `ConnectionStrings:garnet` is present. |
| `Microsoft.Extensions.ServiceDiscovery` | 10.6.0 | `ServiceDefaults` | Resolves logical service names such as `api` to real endpoints. This is what lets the UI run with no configured port. |
| `Microsoft.Extensions.Http.Resilience` | 10.6.0 (`ServiceDefaults`), 10.8.0 (`Infrastructure`) | both | `AddStandardResilienceHandler`: retries, timeouts, circuit breaker. |

`ServiceDefaults` is a shared project (`IsAspireSharedProject`) referenced by both hosts; it is the
single place where discovery, resilience, health checks and OpenTelemetry are configured.

## Web: API host

| Package | Version | Role and reason |
| --- | --- | --- |
| `Microsoft.AspNetCore.OpenApi` | 10.0.9 | Generates the OpenAPI document at `/openapi/v1.json` in Development. |
| `Scalar.AspNetCore` | 2.16.16 | Interactive API reference at `/scalar/v1`. Chosen over Swagger UI for the modern .NET 9+ story. |
| `Swashbuckle.AspNetCore` | 10.2.3 | Referenced but not mapped in `Program.cs` today; kept from the original template. |
| `Serilog.AspNetCore` | 10.0.0 | Host logging integration. |
| `Serilog.Sinks.Console` | 6.1.1 | Console sink. |
| `OpenTelemetry.*` | 1.17.0 | Traces and metrics for the API host process. |
| `Cortex.Mediator` | 3.1.2 | Dispatches the commands and queries in `UseCases` from the endpoint layer. |

## Web: user interface

| Package | Version | Projects | Role and reason |
| --- | --- | --- | --- |
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | 10.0.9 | `WebApp` | Server-side hosting of the Blazor app; interactive **Server** rendering is what pages actually use. |
| `Microsoft.AspNetCore.Components.WebAssembly` | 10.0.9 | `WebApp.Client` | The WebAssembly project exists and is wired, but no page opts into the WebAssembly render mode yet. |
| Tailwind CSS | v4 (standalone CLI, downloaded per machine) | `WebApp/wwwroot` | Styling. The standalone binary avoids a Node.js toolchain entirely; the input is `app.tailwind.css`, the committed output is `app.css`. |

Why Server interactivity rather than WebAssembly for data: the browser would otherwise call the API
directly, which requires CORS and a publicly reachable API base address. The UI currently talks to
the API server-side, which keeps the API private and lets pages be prerendered. The trade-off and its
open question are recorded in [decisions/0005](decisions/0005-url-scheme.md).

## Data and persistence

| Package | Version | Role and reason |
| --- | --- | --- |
| `Microsoft.EntityFrameworkCore` | 10.0.10 | ORM for the accounts layer. |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.10 | Design-time services. Referenced by **both** `Infrastructure` (where the `DbContext` lives) and `WebApi` (the startup project) — EF tooling requires it in the startup project. |
| `Microsoft.EntityFrameworkCore.Tools` | 10.0.10 | `dotnet ef` commands. |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.9 | ASP.NET Core Identity stores. Chosen over hand-rolled user tables: the task was accounts, not authentication internals. |
| `Npgsql` / `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 | PostgreSQL provider. |
| `Ardalis.Specification.EntityFrameworkCore` | 9.3.1 | Specification pattern support for EF Core queries. |
| `Specification` | 1.0.1 | Legacy package from the original template; source of the `NU1701` warnings — see [Known constraints](#known-constraints). |
| `Riok.Mapperly` | 4.3.1 | Source-generated mapping. Chosen over reflection-based mappers: mapping mistakes become compile errors. |
| `Microsoft.Extensions.Caching.Abstractions` | 10.0.10 | `IDistributedCache` abstraction that `Infrastructure` depends on instead of a concrete cache. |

**Why PostgreSQL for accounts:** the local package cache had no EF Core 10 provider for SQLite (newest
was 9.0.9), and pairing a 9.x provider with EF Core 10 is the classic version-skew trap. PostgreSQL
is the only store with a version-compatible provider available. See
[decisions/0004](decisions/0004-storage-choices.md).

**Why JSON files for provider records:** no migration, no server, and the records are diagnostics
rather than transactional data. They are written outside the repository so they can never be
committed.

## Application plumbing

| Package | Version | Projects | Role |
| --- | --- | --- | --- |
| `Cortex.Mediator` | 3.1.2 | `UseCases`, `Shared`, `WebApi` | Command/query dispatch and pipeline behaviours. |
| `ErrorOr` | 2.1.1 | `Shared`, `UseCases` | Result type for domain and application errors instead of exceptions for expected failures. |
| `Riok.Mapperly` | 4.3.1 | `Shared`, `Infrastructure` | DTO and entity mapping. |
| `Microsoft.Extensions.Configuration.*` | 10.0.10 | `Veder.ProviderProbe` | Configuration binding for the operator tool. |
| `Microsoft.Extensions.DependencyInjection` / `Logging` | 10.0.10 | `Veder.ProviderProbe` | Hosting the tool's own service provider. |

## Observability

| Package | Version | Projects | Role |
| --- | --- | --- | --- |
| `OpenTelemetry.Extensions.Hosting` | 1.15.3 (`ServiceDefaults`), 1.17.0 (`WebApi`) | both | Hosting integration. |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.15.2 (`ServiceDefaults`), 1.17.0 (`WebApi`) | both | Incoming request traces. |
| `OpenTelemetry.Instrumentation.Http` | 1.15.1 / 1.17.0 | both | Outbound call traces. |
| `OpenTelemetry.Instrumentation.Runtime` | 1.15.1 | `ServiceDefaults` | Runtime metrics. |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.17.0 | both | OTLP export when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. |
| `Serilog` | 4.4.0 | `Infrastructure` | Structured logging. |
| `Serilog.Sinks.Postgresql.Alternative` | 4.2.0 | `Infrastructure` | Optional database sink. |
| `Serilog.Sinks.Elasticsearch` | 10.0.0 | `Infrastructure` | Optional Elasticsearch sink. |

Version skew note: `ServiceDefaults` pins the 1.15.x instrumentation line while `WebApi` pins 1.17.0.
Both resolve, but a reader should expect the newer API surface in the host and the older in the shared
project.

## Credentials and cryptography

| Package | Version | Role |
| --- | --- | --- |
| `System.Security.Cryptography.ProtectedData` | 10.0.1 | DPAPI on Windows for the credential vault. Without it the vault falls back to AES-256-GCM. |

Credentials are never stored in the repository: the vault keeps only a fingerprint and suffix in
clear text, and connection strings come from environment variables or user-secrets.

## Testing

| Package | Version | Role |
| --- | --- | --- |
| `xunit` | 2.9.3 | Test framework. |
| `xunit.runner.visualstudio` | 3.1.4 | Runner integration. |
| `Microsoft.NET.Test.Sdk` | 18.3.0 | `dotnet test` support. |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.5 | `WebApplicationFactory<Program>` boots the real HTTP pipeline for endpoint tests. |
| `Microsoft.Extensions.Caching.Memory` | 10.0.10 | In-memory cache for tests. |
| `Microsoft.Extensions.Options` | 10.0.10 | Options binding in tests. |

The suite is deliberately dependency-light: no container fixtures, no network. Provider behaviour is
tested against recorded payload shapes, and the endpoints are tested through the real pipeline with
fakes substituted at the provider boundary.

## Operator tooling

`tools/Veder.ProviderProbe` is a console application that exercises every provider capability through
the product's own code path (`IWeatherProvider`, `IGeocodingProvider`, …) rather than through HTTP
probes, so it fails when the product fails. It also manages credential lifecycle
(`credentials set|rotate|revoke|status`) and prints observations from the store.

## Front-end build tooling

| Item | Detail |
| --- | --- |
| Tailwind CSS v4 standalone CLI | Downloaded to `tools/tailwind/tailwindcss.exe` and **gitignored** (112 MB). |
| MSBuild integration | `BuildTailwind` target in `WebApp.csproj`, `BeforeTargets="BeforeBuild"`; if the binary is missing it warns and falls back to the committed `wwwroot/app.css` instead of failing the build. |
| Node.js | Not required, not installed. |
| Design tokens | Defined in `WebApp/wwwroot/app.tailwind.css` (parchment/ink/ochre/sage/terracotta) with the accessibility rule recorded in `docs/ui-accessibility.md`. |

## Known constraints

| Constraint | Detail |
| --- | --- |
| `NU1701` warnings | `Specification 1.0.1` is a legacy package restored as .NET Framework assemblies. It produces warnings on every build (ten in the last full build) and is **not** the cause of any known defect. Removing it is a separate, behavioural change. |
| Warning count | A full solution build reports 29 warnings, 0 errors. The visible ones are `NU1701`; the remainder were not individually audited — see the verification section of the accompanying work log. |
| No `LICENSE`, `CONTRIBUTING` or `CODE_OF_CONDUCT` | Absent from the repository. Add before accepting external contributions. |
| Case-sensitive filesystems | Several tracked file names are lowercase (`veder.tests.csproj`, some `.cs` files) while `Veder.Server.slnx` references PascalCase. Building on Linux/macOS needs `git mv` to reconcile the casing. |
| Offline package source | The development machine restores from a local NuGet cache; only versions already present could be referenced. This is why no SQLite provider appears. |

## Refreshing this inventory

```powershell
# every package reference with its version, per project
Get-ChildItem -Recurse -Filter *.csproj |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  ForEach-Object {
    "--- " + $_.FullName.Replace((Get-Location).Path + '\', '')
    Select-String -Path $_.FullName -Pattern '<PackageReference Include="([^"]+)" Version="([^"]+)"' |
      ForEach-Object { "    " + $_.Groups[1].Value + ' ' + $_.Groups[2].Value }
  }
```

If a version here disagrees with a project file, the project file is correct — fix this document in
the same commit as the change that moved it.
