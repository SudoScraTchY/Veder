# 0001 — Layering and where provider adapters live

**Status:** Accepted · **Date:** 2026-09 · **Applies to:** all projects

## Context

The repository started from a layered template (Domain, UseCases, Infrastructure, WebApi) and the
first real feature was a third-party weather provider. The natural pull is to put "the provider" next
to the thing that consumes it — inside the use case or the endpoint — because that is where the
response shape is known.

That pull is wrong here. Providers are the part most likely to multiply and change: a second provider
was always the plan, and providers differ in *capability*, not just in URL.

## Decision

- `Domain` holds the model and the provider **contracts** (`IWeatherProvider`,
  `IWeatherSeriesProvider`, and one interface per capability: geocoding, elevation, marine, ensemble,
  climate, flood, air pollution). It has no HTTP, storage or provider SDK knowledge.
- Provider **adapters** live in `Infrastructure/Providers`, one folder per provider
  (`OpenMeteo/`), with the wire layer (endpoints, request builder, response parsers, mapper) split
  from the adapter that implements the contracts.
- `WebApi` composes: it selects a provider through `ProviderRegistry`/`ProviderSelector` and never
  reaches into an adapter directly.
- Cross-boundary shapes go through `Shared/Contracts`, not through domain entities.

## Consequences

- A new provider is added by implementing interfaces in `Infrastructure` and registering them — no
  change to `Domain`, and no change to the endpoint logic.
- Capability is a first-class concept (`ProviderCapability` flags), so an endpoint can ask
  "do you support marine?" instead of catching an exception.
- The cost is indirection: reading a single provider request means opening four files
  (`client` → `requestbuilder` → `parser` → `adapter`). That cost is accepted deliberately; the
  provider matrix in [../providers/open-meteo.md](../providers/open-meteo.md) is the payoff.
- `Domain` currently still references the legacy `Specification 1.0.1` package, which is the one
  exception to "no package references"; it produces `NU1701` warnings and is a known cleanup.

## Alternatives considered

- **Provider logic in use cases** — rejected: capability differences would leak into application
  logic and the second provider would require editing use cases.
- **A generic HTTP client with a mapping table** — rejected: open-meteo needs per-endpoint parsing
  (envelopes, units, missing values), which a table cannot express without becoming code anyway.
