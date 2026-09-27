# Veder UI — handoff

What was built in the UI work, how to run it, what is real versus mocked, and what remains.

---

## 1. Running it

```powershell
# the real toolchain path on this machine - the dotnet on PATH has no SDK
& 'C:\Program Files\dotnet\dotnet.exe' build Veder.Server.slnx
& 'C:\Program Files\dotnet\dotnet.exe' test  Veder.Server.slnx

# API (the UI reads this)
& 'C:\Program Files\dotnet\dotnet.exe' run --project WebApi --urls http://127.0.0.1:5199

# UI
$env:ApiBaseUrl='http://127.0.0.1:5199'
& 'C:\Program Files\dotnet\dotnet.exe' run --project WebApp\WebApp --urls http://127.0.0.1:5210
```

Tailwind CSS is compiled by `dotnet build` itself. No Node.js is installed on this machine and none is
required: the standalone CLI (`tools\tailwind\tailwindcss.exe`, v4.3.3) is invoked by the `BuildTailwind`
target in `WebApp.csproj`. That binary is **gitignored** — download it once per machine:

```powershell
Invoke-WebRequest https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-windows-x64.exe -OutFile tools\tailwind\tailwindcss.exe
```

If it is missing the build warns and uses the committed `app.css`, so a fresh clone still builds.

## 2. Screens delivered

| Route | Screen | Data |
|---|---|---|
| `/` | Search and first result | none (navigates to `/weather`) |
| `/weather` | Weather detail: current, daily outlook, air quality, notices, provenance | `GET /api/weather/aggregate` |
| `/providers` | Data sources: capabilities, priority, health | `GET /api/providers` |
| `/about` | How this works: provenance, caching, provider selection | static copy |
| `/account` | Account state | `GET /manage/info` probe |

All five render without an account, which is the product's core promise. `/weather` accepts
`?name=`, `?lat=&lon=` and `?providerId=` so it can be linked to directly.

## 3. Design decisions worth knowing

- **One accent carries every emphasis.** Ochre marks the single action on a screen. Sage and terracotta are
  **semantic, not decorative**: sage means data or healthy, terracotta means something is degraded. If you
  add colour anywhere, keep that mapping or the provenance chips stop meaning anything.
- **The provenance chip is the product's visual grammar.** It is a component
  (`Components/Ui/ProvenanceChip.razor`), not markup to be copied. Its full meaning lives in `title` and
  `aria-label`, so it is not colour-only information.
- **`StateBlock` owns loading, empty and error.** Every data view should use it rather than inventing a
  presentation, so "no data" never looks like "broken".
- **Whitespace over chrome.** Hierarchy comes from type size and weight plus spacing, not from borders and
  boxes. Panels exist only where a group of values needs to be read as a unit.
- **Persian layer is a single attribute.** The `dir="rtl"` toggle on `/weather` flips direction and swaps
  to Vazirmatn; the same markup serves both scripts. Styling is explicitly non-religious.

## 4. What is real and what is not

**Real:** every number on `/weather` and `/providers` comes from the live API, which in turn calls the real
Open-Meteo API and its own cache. There are no fixtures. When the upstream provider fails, the screens show
their error state with the real reason.

**Not wired:** the browser-level account flow. `/account` reports the account service's state and explains
what to configure, but does not register or sign in. The UI and API are separate origins, so a working
browser login needs CORS and cookie configuration (credentials, allowed origins, SameSite) that this work
deliberately did not guess at. The API-level flows — register, login, logout, `/manage/info` — were verified
live against Postgres and are described in `docs/README.md` §7.

**Not verified:** the responsive, keyboard and contrast pass. See §6.

## 5. Known limitations

- **Weather screens lack designed loading/empty/error states.** `StateBlock` exists and is used by
  `/providers` and `/account`; `/weather` and `/` do not yet consume it, so a slow or failing request shows
  the raw error text rather than the designed presentation.
- **`/providers` is read-only.** Capability flags and health are displayed, but there is no UI to enable,
  disable or re-prioritise a provider, and no audit view. The API supports the first two through the
  registry; nothing in the UI calls them.
- **No screenshot evidence is committed.** The verification pass in §6 has not been run, so claims about
  breakpoints and contrast are unproven.
- **`/` has no location detection.** It offers quick-start places and a coordinate shortcut instead; browser
  geolocation was not added because it triggers a permission prompt on load.

## 6. Verification status

Established with evidence:

| Check | Result |
|---|---|
| Build | `dotnet build Veder.Server.slnx` → 0 errors, at every commit |
| Tests | `dotnet test` → 109 passed, 0 failed |
| Screens render | all five at HTTP 200 against running services |
| Navigation | every screen linked from every page |
| Live data | `/providers` rendered the real registry (capability chips, health states); `/weather` rendered a real temperature and provenance chip |
| Git | six commits pushed to `origin`, fast-forward only, history linear, working tree clean |

Not yet established:

| Check | Status |
|---|---|
| 360px / tablet / desktop layout capture | **not run** |
| Keyboard-only walk with visible focus | **not run** (focus styling is implemented; not exercised) |
| WCAG AA contrast measurement | **not measured** (palette chosen for it; not verified) |
| Deliberate loading / empty / forced-failure states | **not run** for the weather screens |

## 7. Suggested next steps

1. Wire `StateBlock` into `/weather` and `/`, with a skeleton that reserves height.
2. Run the verification pass and commit the captures as evidence.
3. Configure CORS + cookie credentials, then build the real `/account` forms.
4. Add provider administration to `/providers` (enable, disable, re-prioritise) with an audit view.
5. Consider a second provider adapter: it would let the UI's `substituted` chip state be demonstrated live
   rather than only unit-tested.

## Ports: pick a range Windows has not reserved

A startup that dies with `SocketException 10013` ("An attempt was made to access a socket in a way forbidden by its
access permissions") on `Socket.Bind` is **not** an application fault and **not** "port already in use" (that is
`10048`). The port fell inside a Windows *excluded* TCP range. Check before reporting a startup bug:

```powershell
netsh int ipv4 show excludedportrange protocol=tcp
```

At the time of writing `5141-5240` was excluded, which covers the `5199`/`5210` this document originally used — the
ranges shift with container/WSL activity, so a port that worked earlier can stop working later with no code change.
The interface was last verified running on `5390`/`5391`:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
& 'C:\Program Files\dotnet\dotnet.exe' WebApi\bin\Debug\net10.0\WebApi.dll --urls http://127.0.0.1:5390
$env:ApiBaseUrl='http://127.0.0.1:5390'
& 'C:\Program Files\dotnet\dotnet.exe' WebApp\WebApp\bin\Debug\net10.0\WebApp.dll --urls http://127.0.0.1:5391
# then open http://127.0.0.1:5391/weather
```

## Fixes found by verification, not by review

Worth keeping in mind when extending the interface, because each looked correct in isolation:

- **The layout shell had no styling.** A class-coverage audit (every class in markup checked against the generated
  CSS) found 13 classes with nothing behind them — `page`, `sidebar`, `top-row`, `text-danger`, the
  `components-reconnect-*` helpers — all leftovers from the template's Bootstrap era. `MainLayout` was rebuilt on the
  design tokens and now carries the skip-to-content link. Re-run that audit if markup moves between files.
- **Colour: 500 shades are fills and borders, 700 shades are text.** The measured case is in
  `docs/ui-accessibility.md`; `ochre-500` as text is 2.86:1.
- **Every interactive control needs a visible focus ring**; one screen shipped with an `outline-none` input and only a
  border-colour change, which is the invisible-focus anti-pattern.
- **An unmatched place is an empty result, not an error.** Geocoding failure returns 404 and is presented as empty
  (`role="status"`), not as a failure (`role="alert"`).

## Repository state

Twelve commits on `feat/phase3-4-provider-selection`, fast-forward pushes only, `main` untouched, working tree clean.
The rendered pass — breakpoint captures and a keyboard-only walk — still needs the AutoClaw browser panel opened.
