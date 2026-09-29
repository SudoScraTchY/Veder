# 0006 — Portability, naming and the ports that lie

**Status:** Accepted (one item requires a decision) · **Date:** 2026-09 · **Applies to:** repository-wide

## Context

Three unrelated annoyances share one root cause — the tooling that wrote files into this repository
normalised file-name casing, and the developer's operating system is tolerant in ways that a CI
runner will not be.

1. Several tracked files are **lowercase** (`tests/Veder.Tests/veder.tests.csproj`,
   `tools/Veder.ProviderProbe/veder.providerprobe.csproj`, and several `.cs` files such as
   `iprovidercallrecordstore.cs`, `geobucket.cs`, `identityregistration.cs`).
   `Veder.Server.slnx` references them in **PascalCase**. Windows matches case-insensitively, so the
   solution builds here and would fail to resolve those projects on a case-sensitive filesystem.
   Blazor component files were affected too, but Razor *requires* a leading uppercase name
   (`RZ10011`), so those were corrected to PascalCase and are fine.
2. The Tailwind CLI is a 112 MB binary. Committing it would bloat the repository; not having it would
   break the build.
3. Ports are chosen by hand, and on Windows a hand-picked port can land inside an OS-excluded TCP
   range, where `bind` fails with `SocketException (10013)` — "access to a socket forbidden by its
   access permissions". This is a permission error, not "port in use" (`10048`), and Kestrel treats it
   as fatal: the host prints its startup banner and then dies, which reads like an application crash.

## Decision

1. **Accept the lowercase names as they are**, and stop normalising new files. C# compilation is
   unaffected (the compiler does not care about `.cs` file names); the *project file* names are the
   real liability, and they are documented here rather than silently renamed. A `git mv` series to
   reconcile casing is required **before** any Linux/macOS CI is added.
2. **Gitignore the Tailwind binary** and make the build tolerant: `BuildTailwind` runs
   `BeforeTargets="BeforeBuild"` only when the binary exists, and otherwise warns and keeps the
   committed `wwwroot/app.css`. A fresh clone therefore builds with no download step, and a developer
   who wants to change styles downloads the CLI once.
3. **Choose ports deliberately and check them.** Launch profiles use `5680`/`5681` (API) and
   `5690`/`5691` (UI) — outside the excluded range observed on the development machine
   (`5141–5240`). `tools/check-ports.ps1` fails loudly when a needed port is excluded, and
   `docs/runbook-startup-and-ports.md` documents the diagnosis.

## Consequences

- The repository builds on Windows today and is **not** claimed to build on Linux/macOS until the
  casing is fixed. This is a real portability gap, stated rather than implied.
- There is deliberately no `global.json`: any .NET 10 SDK builds the solution, which suits a
  single-developer repository. Pinning the SDK is an easy later addition if that changes.
- Style changes require a one-time download; the committed CSS always works as a fallback, so a
  contributor who only touches C# never needs it.
- The port runbook exists because this failure mode cost hours: it presents as a crash, and the only
  way to see the truth is `netsh int ipv4 show excludedportrange protocol=tcp`.

## Alternatives considered

- **Commit the Tailwind binary** — rejected: 112 MB in git history for a build-time convenience.
- **Require Node.js and use the npm-distributed CLI** — rejected: adds a second toolchain to a .NET
  repository for styling.
- **Random (port 0) ports everywhere** — rejected for local development: a stable URL is what lets a
  developer keep a bookmark, and the AppHost already assigns dynamic ports when orchestrated.
