# Startup and ports: why the API "started and then stopped working"

Audience: whoever runs or debugs this solution next.
Status: fixed on `feat/phase3-4-provider-selection` (working tree, uncommitted at time of writing).

---

## 1. Symptom

Two different things were being reported as one:

**a. The Blazor UI started and every call to the API failed.**

```
info: System.Net.Http.HttpClient.api.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5199/api/weather/aggregate?*
info: System.Net.Http.HttpClient.api.ClientHandler[104]
      HTTP request failed after 4583.3924ms
      System.Net.Http.HttpRequestException: No connection could be made because the target machine
      actively refused it. (localhost:5199)
       ---> System.Net.Sockets.SocketException (10061): No connection could be made because the
      target machine actively refused it.
warn: WebApp.Components.Pages.Weather[0]
      Aggregated weather request failed
```

**b. Run from the launch profiles, the host died while binding.**

```
System.IO.IOException: Failed to bind to address http://localhost:5270.
 ---> System.Net.Sockets.SocketException (10013): An attempt was made to access a socket in a way
forbidden by its access permissions.
```

And in the Aspire console the same API appeared to start twice on different ports
(`10034/10035`, then `11000/11001`), because Aspire assigns a fresh pair on every run.

## 2. Root cause

There are two defects, and they share one origin: **the project's ports and its API address were
chosen without reference to what the machine will actually allow.**

### 2a. Windows reserved the ports the project was configured to use

`netsh interface ipv4 show excludedportrange protocol=tcp` on this machine:

| Range | Contains |
|---|---|
| 5041 - 5140 | |
| **5141 - 5240** | **5199** (the UI's hard-coded API address), **5210** (the documented UI port) |
| **5241 - 5340** | **5270** (WebApi `http` profile), **5273** (WebApp `http` profile) |
| 5357, 5458 - 5557, 5558 - 5657 | |
| 13367 - 13766, 13874 - 13973 | |
| 50000 - 50059 | (administered) |

A socket opened inside an excluded range fails with `SocketException (10013)`. Kestrel treats that
as fatal: the host prints its banner and then dies. This is invisible in a diff, because `5270`
looks like a perfectly good port.

Verified directly:

```
> dotnet run --project WebApi --launch-profile http
Failed to bind to address http://localhost:5270  (SocketException 10013)

> Test bind on 5199
BIND FAILED on 5199: An attempt was made to access a socket in a way forbidden by its access permissions
```

These ranges are assigned by Hyper-V, WSL and Docker and **move between reboots**. Treat them as
environment state, not a constant.

### 2b. The UI had a hard-coded API address and was not an Aspire resource

`WebApp/WebApp/Program.cs` did this:

```csharp
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5199");
});
```

and never called `AddServiceDefaults()`. Consequences:

- The AppHost's `WithReference(api)` reached nothing: without service discovery, the UI cannot see
  the endpoint the orchestrator published for the API.
- So the UI always dialled `http://localhost:5199`, which is (i) not the API's port in any run
  profile and (ii) inside the excluded range above, so nothing could ever be listening there.
- Health checks, resilience and telemetry were also missing from the UI.

Under Aspire the API was listening on a fresh pair every run, and Aspire published it through its
own proxy on the launch profile's `https` port. The UI ignored all of it.

## 3. What changed

| File | Change |
|---|---|
| `WebApi/Program.cs` | Dropped `AddControllers()` / `MapControllers()` — the sample controller was the only controller. Added `AddAuthorization()` **explicitly**, because `UseAuthorization()` had been getting those services as a side effect of `AddControllers()`. |
| `WebApp/WebApp/Program.cs` | Added `AddServiceDefaults()`. Resolves the API base address as: `ApiBaseUrl` → the AppHost-injected `services:api:*` endpoint → `http://localhost:5680`. Logs the resolved address at startup. |
| `WebApp/WebApp/WebApp.csproj` | Added the `ServiceDefaults` project reference. |
| `WebApi/Properties/launchSettings.json` | `http` 5270 → **5680**, `https` 7128/5270 → **5681/5680**. |
| `WebApp/WebApp/Properties/launchSettings.json` | `http` 5273 → **5690**, `https` 7140/5273 → **5691/5690**. |
| `WebApi/Endpoints/AggregatedWeatherEndpoints.cs` | Removed the duplicated `using` (warning CS0105) and the comments that existed only to explain the sample class's shadowing; un-qualified `Domain.Entities.WeatherForecast`. |
| `WebApi/WebApi.http` | Points at real endpoints instead of the deleted sample route. |
| `tools/check-ports.ps1` | New. Fails when any configured port sits in a Windows excluded range. |
| Removed | `WebApi/Controllers/WeatherForecastController.cs`, `WebApi/WeatherForecast.cs`, `WebApp/WebApp.Client/Pages/Counter.razor` (orphan, no nav link), `UseCases/IServices/{IForcastService,IWeatherService,IAirPullotion}.cs` (empty, unreferenced). |

Ports 5680/5681/5690/5691 were verified bindable and sit outside every excluded range on this
machine.

### Why the new default works in both modes

- **Standalone** (`dotnet run --project WebApi`): the launch profile binds `5680`, and the UI's
  fallback is the same `5680`. No environment variables needed.
- **Under the AppHost**: `WithReference(api)` injects the endpoint Aspire actually published, the
  UI prefers that over the fallback, and `AddServiceDefaults()` supplies discovery and resilience.

## 4. How to run it

```powershell
# always use the real SDK: the dotnet on PATH is a runtime-only shim with no SDK
$env:DOTNET_ROOT='C:\Program Files\dotnet'
$env:PATH="C:\Program Files\dotnet;$env:PATH"

& 'C:\Program Files\dotnet\dotnet.exe' build Veder.Server.slnx
& 'C:\Program Files\dotnet\dotnet.exe' test  Veder.Server.slnx

# preflight: are the configured ports usable on this machine right now?
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-ports.ps1
```

**Everything under Aspire** (needs Docker for Garnet and MongoDB):

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project AppHost\AppHost.csproj
# API  https://localhost:5681   http://localhost:5680
# UI   https://localhost:5691   http://localhost:5690
```

**The API and UI on their own** (no Docker needed — the API falls back to an in-memory cache):

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project WebApi
& 'C:\Program Files\dotnet\dotnet.exe' run --project WebApp\WebApp
```

**If a port is blocked**, override it without touching any file. Both sides must be told:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project WebApi --urls http://127.0.0.1:6001
$env:ApiBaseUrl='http://127.0.0.1:6001'
& 'C:\Program Files\dotnet\dotnet.exe' run --project WebApp\WebApp --urls http://127.0.0.1:6002
```

## 5. How to confirm the fix

1. `powershell -File tools\check-ports.ps1` → *"Every configured port is bindable."* (exit 0).
2. Start the API, then read its banner: it must print `Now listening on: http://localhost:5680`
   **and** `https://localhost:5681`. A `Failed to bind` line here is the old defect.
3. Start the UI and read its banner: `Veder UI: aggregated API base address is http://localhost:5680`.
   That line is new; it makes the UI's target explicit instead of a guess.
4. `curl -k https://localhost:5681/health` → `200` and the body `Healthy`.
5. Load the UI's `/weather`, search any place, and confirm a reading with a provenance chip rather
   than *"Could not load weather data."*
6. Confirm the sample route is gone: `curl -k https://localhost:5681/weatherforecast` → `404`.

## 6. Notes and traps

- **The `dotnet` on `PATH` is a runtime-only shim.** `DOTNET_ROOT` must point at
  `C:\Program Files\dotnet`, or child processes fail to launch. When the AppHost is started without
  it, the Aspire dashboard dies with *"Framework 'Microsoft.NETCore.App', version '10.0.0' not
  found"* and **the resources never start at all** — which looks exactly like the bug being fixed
  here. Check `DOTNET_ROOT` before concluding anything about the app.
- **`AddControllers()` used to be load-bearing.** It registered the authorization services that
  `app.UseAuthorization()` requires. Removing it without adding `AddAuthorization()` broke 8
  integration tests with `Unable to find the required services ... 'AddAuthorization'`. The
  dependency is now explicit.
- **The excluded ranges move.** After a reboot or a Docker/WSL change, re-run
  `tools/check-ports.ps1` before blaming the code.
- **Aspire proxies the launch-profile ports.** Under the AppHost the addresses you call are the
  ones in `launchSettings.json`; the processes themselves listen on random ports. Do not hard-code
  the random ones.
- **Do not run the standalone `https` profile while the AppHost is running**: both want the same
  ports from `launchSettings.json`.

## 7. Removing the sample code

Deleted, after confirming nothing references them:

| Removed | Why |
|---|---|
| `WebApi/Controllers/WeatherForecastController.cs` | The template's sample controller; also collided with a real endpoint name (`GetWeatherForecast`). |
| `WebApi/WeatherForecast.cs` | The template's sample model. It shadowed `Domain.Entities.WeatherForecast`, which forced explicit qualification in real code. |
| `WebApp/WebApp.Client/Pages/Counter.razor` | Template sample page with no link in `NavMenu`. |
| `UseCases/IServices/IForcastService.cs`, `IWeatherService.cs`, `IAirPullotion.cs` | Empty interfaces, referenced by nothing. |

`AddControllers()` / `MapControllers()` went with them; re-adding the pair is a two-line change if
controllers are wanted again. The host still serves everything it did before: `/health`, `/alive`,
`/api/weather/*`, `/api/geocoding/search`, `/api/providers`, and the optional Identity endpoints.
