# 0002 — Cache identity: what a reading is cached *as*

**Status:** Accepted · **Date:** 2026-09 · **Applies to:** `Domain/Caching`, `Infrastructure/Caching`

## Context

The cache had to answer "is this the same reading?" for requests that differ by fractions of a
degree of latitude, by provider, by variable set and by intended freshness. Two candidate rules were
on the table:

1. **Round the coordinates** to a fixed number of decimals and use the rounded pair as the key.
2. **Floor** the coordinates onto a grid and look for a neighbour cell.

Rounding looks equivalent and is not. It is culture-dependent — under a Persian locale an interpolated
`double` renders as `35,700` rather than `35.700`, and the comma is the separator used inside the key
itself. Rounding also collides either side of the ±180° seam and behaves badly near the poles, where
a degree of longitude shrinks to nothing.

A second question was harder: when a request is answered by a *substitute* provider, what key does
that payload get stored under? The tempting answer — "under the requested provider, so the next caller
gets the hit" — is **data poisoning**: a later caller who asked for provider A receives provider B's
data with no indication.

## Decision

- **Freshness identity is a grid cell, not a rounded value.** `GeoBucketer.For` floors coordinates
  onto a cell (default `0.02°`, minimum `0.005°`), `Neighbourhood` allows an adjacent cell to satisfy a
  lookup, and `WrapLongitude` handles the ±180° seam. Above `75°` latitude the cell widens because
  meridians converge.
- **The cache key contains the provider.** `WeatherCacheKey.Compose` puts the provider id inside the
  key, so two providers cannot collide.
- **A payload is only ever cached under the provider that produced it.** A substitute is stored under
  its own key; the response records `requestedProvider` vs `sourceProvider` so the caller can see what
  happened. Nothing is written under the requested provider's key.
- **Rounding is not used anywhere in the key path.** Coordinate formatting is culture-invariant.

## Consequences

- Cache provenance is honest: a hit means "this reading came from this provider", always.
- Failover does not poison the cache, at the cost that a failover hit does not warm the requested
  provider's entry. That is the correct trade: correctness over cache efficiency.
- Cell size is configurable (`Aggregation:GeoCellDegrees`) because the right value depends on terrain
  and on how much spatial precision the product wants to promise.
- `CacheTtlPolicy.For` sets per-data-type freshness (current 7 min, air quality 45 min, forecast and
  marine 4 h, archive 24 h, elevation and geocoding 30 days) with ±10 % jitter, so a fleet of callers
  does not expire together.
- Telemetry records aggregated counters rather than one record per hit; recording every hit was
  measured as not viable (≈9.5k writes/s at expected rates). Full-fidelity events are kept for miss,
  eviction, failure and fallback only.

## Alternatives considered

- **Round-then-key** — rejected for the culture bug, the seam, and poles (all three were found by
  tests, not review).
- **Cache the substitute under the requested key** — rejected as data poisoning.
- **Record every cache hit durably** — rejected on write volume; counters cover the operational need.
