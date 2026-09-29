# 0004 — Storage choices: JSON files, PostgreSQL, and a MongoDB that does nothing yet

**Status:** Accepted (with one open item) · **Date:** 2026-09 · **Applies to:** `Infrastructure/Persistence`, `Infrastructure/Identity`, `AppHost`

## Context

Three different kinds of state needed a home:

1. **Provider records** — call records, diagnostics and observations. Diagnostics, append-mostly,
   queried by operators rather than by traffic.
2. **Accounts** — users and roles, with the ASP.NET Core Identity schema.
3. **Saved locations** — small per-user data, not yet exposed in the UI.

The development machine restores NuGet from a local cache, which constrained the options dramatically:
there is **no EF Core 10 provider for SQLite** (the newest available was 9.0.9), and pairing a 9.x
provider with EF Core 10.0.10 is the classic version-skew trap.

## Decision

- **Provider records are JSON files outside the repository**
  (`ProviderStore:RootPath`, default `%LOCALAPPDATA%\Veder\provider-store`). Append-only per record
  kind, with a manifest, written through a single file abstraction. Outside the repo so they can never
  be committed.
- **Observations are idempotent by construction.** `ObservationRecord.Id` is
  `provider|kind|bucket|observedAt`, so re-recording the same observation replaces it rather than
  duplicating it. This was verified live: repeated probe runs kept the count stable.
- **Accounts use PostgreSQL** through Npgsql, in schema `identity`, with a separate migrations history
  table (`__identity_migrations`). PostgreSQL is the only store with a version-compatible EF Core
  provider available locally.
- **Identity is optional by construction.** With no `ConnectionStrings:VederIdentity`, the identity
  endpoints are not mapped at all and the application boots normally. This was verified: with the
  credential absent, `/register` returns `404` while the weather endpoints return `200`.
- **Saved locations stay in memory** (`InMemorySavedLocationRepository`) until there is a product
  requirement to persist them.
- **MongoDB is declared by the AppHost and consumed by nothing.** This is knowingly incomplete and
  recorded here rather than quietly removed.

## Consequences

- No migrations and no server are needed for provider diagnostics, which keeps the operational
  surface small; the cost is that these records are not queryable in SQL and are bounded by a
  `MaxQueryCount` (default 500) rather than by indexes.
- Accounts are an add-on: a deployment without PostgreSQL simply runs without them, which is what
  makes the weather product independent of the accounts layer.
- **Open item:** the MongoDB resource in `AppHost.cs` starts a container that no application project
  talks to (there is no MongoDB driver reference anywhere). It was added as the intended home for
  durable storage and never wired up. Either implement it or remove it — until then, treat the
  container as provisioning noise.
- The development database password that briefly lived in tracked configuration was removed; the
  published value requires rotation because it remains in history.

## Alternatives considered

- **SQLite for accounts** — rejected: no EF Core 10 provider in the local package source.
- **MongoDB for provider records** — deferred: JSON files did the job with no driver dependency, and
  the observation volume is operator-scale, not traffic-scale.
- **One database for everything** — rejected: it would make the weather product's availability depend
  on the accounts database.
