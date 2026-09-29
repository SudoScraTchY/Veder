# 0005 — URL scheme, and why the interface does not use WebAssembly for data

**Status:** Accepted (with two open items) · **Date:** 2026-09 · **Applies to:** `WebApp`

## Context

The interface originally addressed a reading as
`/weather?lat=35.6892&lon=51.389`. The product is deployed at `veder.ir`, where **the domain is the
product** — a weather site whose readings live behind a `/weather` prefix and two opaque query
parameters reads like a search result rather than a page, and it is not the shape a person shares or
a crawler indexes.

Separately, the Blazor project declares both interactive **Server** and interactive **WebAssembly**
render modes, and nothing uses WebAssembly. The obvious question is whether moving data fetching to
the browser would improve the situation.

## Decision

**Identity in the path, options in the query string.**

```
/Tehran                 a place            (also /tehran — matching is case-insensitive)
/35.6892,51.389         coordinates
/new-york               a place with a space in its name
/Tehran?providerId=…    options stay in the query
/weather?name=Tehran    legacy shape, still resolved
```

- `WebApp/Routing/PlaceRoute.cs` owns the shape: `ForPlace`, `ForCoordinates`, `ToSlug`,
  `ToDisplayName`, and culture-invariant coordinate formatting.
- Routes are declared on the weather page: `/weather`, `/{Place}`, and
  `/{Latitude:double},{Longitude:double}`. The `:double` constraints keep a coordinate-shaped segment
  from being treated as a place name.
- Route resolution sits in `OnParametersSetAsync` (not `OnInitializedAsync`) so back and forward
  navigation re-resolve correctly; a route key prevents refetching the same reading on re-render.
- Searching from the weather page **navigates** rather than re-rendering in place, so the address bar
  always shows the canonical URL.

**WebAssembly is not used for data fetching.** The browser calling the API directly would require
opening the API with CORS and exposing a publicly reachable base address; the UI instead reads the API
server-side, which keeps the API private and lets pages prerender. The URL shape is a routing concern
and does not change under either choice.

## Consequences

- Readings are shareable and indexable; the address bar is meaningful.
- `PlaceRoute.ToSlug` replaces spaces with dashes and escapes the rest, so Persian place names work
  as percent-encoded path segments.
- **Open item 1 — soft 404s.** An unknown place returns HTTP `200` with the empty state
  ("No place matched that name") instead of a `404`. Search engines may index non-existent places.
  The fix requires setting the response status during prerender; it was deliberately not attempted
  here because it needs care with Blazor's render pipeline.
- **Open item 2 — the WebAssembly mode is dead weight.** `App.razor` declares
  `.AddInteractiveWebAssemblyRenderMode()` and the `WebApp.Client` project is referenced, but no page
  opts in. Either make a page genuinely client-interactive or drop the project; keeping the current
  state costs build time and misleads readers.
- A client-side redirect from the legacy `?name=` form to the canonical path was considered and
  rejected: `NavigationManager.NavigateTo` is not allowed during the prerender pass, so it would have
  turned a cosmetic improvement into a rendering hazard.

## Alternatives considered

- **Keep query parameters, prettify with a rewrite** — rejected: a proxy that hides the real URL does
  not make the address bar meaningful.
- **`/w/{place}`** — rejected as a shorter version of the redundant prefix.
- **WebAssembly for instant navigation** — rejected for this product: it buys client-side routing at
  the cost of exposing the API, and the pages are prerendered today.
