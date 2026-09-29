# 0003 — Provider failure policy: classify once, act consistently

**Status:** Accepted · **Date:** 2026-09 · **Applies to:** `Domain/Caching/ProviderFailurePolicy.cs`

## Context

With more than one provider, every request raises the same three questions, and answering them
locally at each call site guarantees inconsistent behaviour:

1. Does this failure mean the provider is unhealthy?
2. May I answer with a different provider instead?
3. What does the caller get told?

An early iteration treated any failure as "provider down" and degraded that provider globally. That
is wrong in both directions: a 404 for marine data in a landlocked region is not ill-health, and one
provider's marine gap must not degrade its forecast capability for everyone.

## Decision

Classify every failure once, in `ProviderFailurePolicy`, and let that classification drive both health
and substitution:

| Failure kind | Affects provider health | Allows substitution |
| --- | --- | --- |
| Availability (connection, 5xx), timeout, rate limited, malformed response | Yes | Yes |
| Authentication, invalid request, unsupported capability | No | No |

- Health is tracked **per provider**, never globally.
- `MaySubstitute(requestedProviderId, strictProvider, kind)` is the single gate for substitution and
  is where `strictProvider=true` is honoured — the caller then receives a `503` with the reason
  instead of another provider's answer.
- `ToOutcomeName` produces the outcome recorded in the response's provenance block.

## Consequences

- An inland marine 404 cannot degrade anyone's forecast; a genuine outage degrades only the provider
  that is out.
- A malformed response is treated as an availability problem on purpose: if the wire format breaks,
  the provider is not usable even though it answered.
- Substitution is observable rather than silent: every aggregated response carries `failover` and
  `degraded` flags plus the source provider name.
- Adding a new failure kind means changing one file and its tests, not auditing call sites.
- The classification is deliberately conservative: an authentication failure never triggers
  substitution, because a bad key is an operator problem that silently masking it would hide.

## Alternatives considered

- **Retry harder instead of substituting** — already done at the transport layer
  (`AddStandardResilienceHandler`: 3 retries, exponential backoff with jitter, `Retry-After`
  honoured, circuit breaker). Substitution covers what retries cannot: an outage that outlives the
  retry budget, and a provider that lacks the data entirely.
- **Global provider health** — rejected; it couples unrelated capabilities.
