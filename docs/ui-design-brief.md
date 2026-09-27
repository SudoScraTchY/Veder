# Veder UI — design brief

Scope: the web interface for the Veder weather and air-quality service. This brief names every screen,
the primary flow through them, the visual direction, and the component/state inventory, so the work is
unambiguous rather than improvised screen by screen.

---

## 1. What the interface is for

Veder answers one question — *what is the weather and air quality here, and where did that answer come
from?* — and it must answer it **without an account**. The interface therefore has two jobs: present
current conditions and the outlook compactly, and be honest when a reading is stale or served by a
different provider than the one requested.

That second job is the product's differentiator, so it is a first-class UI concern, not a footnote.

## 2. Screens

| # | Route | Screen | Purpose |
|---|---|---|---|
| 1 | `/` | **Search & first result** | Enter a place, see current conditions immediately. Doubles as the landing screen; no marketing page. |
| 2 | `/weather` | **Weather detail** | Full aggregated view: current, daily outlook, air quality, notices, provenance. Also accepts `?lat&lon&name&providerId&strictProvider`. |
| 3 | `/providers` | **Data sources** | Every configured provider with its capabilities, priority, health and quota; states which one is currently serving. |
| 4 | `/account` | **Account (optional)** | Register, sign in, sign out, view the signed-in identity. Reachable but never required; explains that everything works without an account. |
| 5 | `/about` | **How this works** | Short, plain-language explanation of caching, provider selection and freshness — where the provenance chips are finally explained. |

Deliberately **not** building: an admin console (no auth roles yet), a map view (no tile source), and a
settings page (nothing user-configurable that is not a URL parameter today).

## 3. Primary user flow

```
/  →  type a place (or use the detected default)  →  submit
   →  /weather?name=…  →  current conditions + provenance chip
      →  read daily outlook and air quality
      →  if a notice appears ("served by X", "stale"), click through to /about
      →  optionally: /providers to see which source answered and why
      →  optionally: /account to save preferences later (not required)
```

Everything up to and including the weather detail is reached **without authentication**; `/account` is a
detour, never a gate.

## 4. Visual direction

**Structural DNA: "breathing whitespace" (preset 11).** Generous empty space, heading and body weights
separated by little more than weight and size rather than by rules and boxes, one accent colour carrying
every emphasis, and section rhythm driven by a shared spacing scale rather than ad-hoc margins.

**Palette: the project's established cartographic tokens.** This is an intentional adaptation, and worth
stating plainly. The product already ships a documented design system — parchment ground, warm ink,
ochre accent, sage secondary, terracotta reserved for degradation — built in an earlier increment at the
user's explicit direction ("cartographic warm data"). Replacing that palette now would discard a working,
tested brand for no functional gain, so the **layout discipline** of preset 11 is adopted while the
**colour and type identities stay as established**. Emphasis still resolves to a single accent (ochre);
sage and terracotta are semantic, not decorative — sage means data, terracotta means *something is
degraded*.

Net effect: preset 11's restraint, on the palette the product already owns.

## 5. Design tokens

Defined once in `WebApp/WebApp/wwwroot/app.tailwind.css` under `@theme`, consumed as Tailwind utilities.

| Group | Tokens | Use |
|---|---|---|
| Ground | `parchment-50 / 100 / 200 / 300` | page ground, panels, hairlines |
| Type | `ink-500 / 700 / 900` | secondary, body, headings |
| Accent | `ochre-300 / 500 / 700` | the single emphasis colour: primary actions, active states |
| Data | `sage-300 / 500 / 700` | measured values, healthy states, capability chips |
| Alarm | `terracotta-500 / 700` | staleness, substitution, failures |
| Type scale | `--font-sans` (Vazirmatn → system stack), `--font-mono` | prose and numerals |
| Shape | `--radius-card`, `--shadow-card` | panels only; no other elevation |

**Spacing** follows Tailwind's 4px scale with a deliberate preference for the larger end (`gap-5`/`gap-8`,
section spacing ≥ 2rem) — that is where "breathing" comes from. **Numerals** are tabular so values do not
jitter as they refresh.

## 6. Component inventory and required states

| Component | States to implement |
|---|---|
| `LocationSearch` | default, filled, submitting (disabled + "fetching"), validation error |
| `ProvenanceChip` | cached, fresh-from-provider, substituted, stale/degraded |
| `CurrentConditions` | loaded, loading (skeleton), unavailable |
| `DailyOutlook` | loaded, fewer than 7 days, empty (no outlook available) |
| `AirQualityPanel` | loaded, unavailable, suppressed by request |
| `NoticeList` | hidden when empty, listed when not |
| `ProviderTable` | loaded, all-unhealthy, empty registry |
| `AccountForm` | default, invalid input, wrong password, submitting, success |
| `PageShell` | nav with keyboard focus, RTL toggle, active route |

Hover, `focus-visible`, active and disabled styling are required for every interactive element; focus
rings are visible against both parchment and panel backgrounds. Loading uses reserved-height skeletons so
nothing jumps. Empty and error states are written copy, never a blank region.

## 7. Responsive and accessibility targets

- **360px → wide desktop**, single-column below `md`, two columns at `md`, three-plus at `lg`. No
  horizontal overflow; long place names and Persian text wrap rather than clip.
- Semantic landmarks (`header`, `nav`, `main`, `section` with headings), one `h1` per screen, labelled
  form controls, `aria-live="polite"` on the result region so updates are announced.
- Fully keyboard operable: tab order follows reading order, `Enter` submits the search, the RTL toggle is
  a real `button`, and no interaction depends on hover.
- Text contrast targets WCAG AA (≥ 4.5:1 body, ≥ 3:1 large). The palette was chosen with this in mind;
  each pairing is verified rather than assumed.
- `prefers-reduced-motion` respected (no transitions beyond colour and opacity).

## 8. Data and mocking

All screens read the live API (`GET /api/weather/aggregate`, `/api/providers`), which already runs without
credentials. Nothing is faked: when the upstream provider is unreachable the screen shows its **error
state** with the real reason, which is more useful than a fixture. `/account` talks to the real Identity
endpoints when `ConnectionStrings:VederIdentity` is configured, and shows a clear "accounts are not
enabled on this deployment" state when it is not — that is a designed state, not a broken one.

## 9. Out of scope

New backend endpoints, authentication as a requirement, deployment/CI, marketing copy, logo or licensed
assets, and any refactor of code the UI does not touch.
