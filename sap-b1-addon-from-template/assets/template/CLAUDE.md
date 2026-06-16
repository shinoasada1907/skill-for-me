# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A starter template for a **SAP Business One Add-On** built on .NET 10 (`net10.0-windows`, WinForms enabled). It connects to a running SAP B1 client through the **UI API** (`SAPbouiCOM`) and the **DI API** (`SAPbobsCOM`), and optionally talks to the **Service Layer** REST API. The codebase is intentionally skeletal — most files are base classes with `TODO`/example markers meant to be extended for a concrete Add-On.

## Build

```powershell
dotnet build TemplateAddOnDotNetCore.slnx
```

There are no tests in this template. The add-on cannot be meaningfully run from a dev box alone — it attaches to a live SAP Business One client process (see Runtime below).

> **SDK reference required before building.** Every project references `SAPBusinessOneSDK.dll` via a hard-coded `HintPath` of `C:\Program Files (x86)\SAP\SAP Business One SDK\Lib\SAPBusinessOneSDK.dll`. If the build fails on missing SAP types (`SAPbouiCOM`/`SAPbobsCOM`), fix the `HintPath` in the `.csproj` files — do not remove the reference. There are commented-out `TODO` reference blocks in the csproj files as placeholders.

## Architecture

Four projects, strict one-directional dependency flow (Presentation → BLL → DAL → Common):

- **`TemplateAddOnDotNetCore`** (Presentation, `WinExe`) — entry point and SAP event handling.
- **`TemplateAddOnDotNetCore.BLL`** — business logic services (`BaseService`).
- **`TemplateAddOnDotNetCore.DAL`** — SAP data access (DI API, query building, Service Layer client).
- **`TemplateAddOnDotNetCore.Common`** — constants, enums, helpers; no SAP references except the SDK.

### Startup / connection flow (the key thing to understand)

`Program.Main` → `new App()`. `App : SapUiBase`, and the entire lifecycle runs **inside the `SapUiBase` constructor** (`Base/SapUiBase.cs`):

1. Connects to the **UI API** (`Application`), using `args[1]` as the connection string when launched by SAP, otherwise a parameterless `new Application()`.
2. `InitializeDiApi()` — derives a DI API `Company` from the UI connection context (`GetContextCookie` → `GetConnectionContext` → `SetSboLoginContext` → `GetDICompany`), then **hands it to the DAL** via `SapDiBase.Company = company`. This is the bridge between the Presentation and DAL layers.
3. On assigning `SapDiBase.Company`, `SapDiBase` auto-detects HANA vs SQL Server (`DbType`) — this drives dialect differences everywhere downstream.
4. Loads user context (`UserId`, `BranchName`) into static `SapDiBase` fields.
5. Calls `Loading()` (override point), `RegisterEvents()`, then `oApp.Run()` (blocks).

**Implication:** `SapDiBase` is a static, process-global singleton holding the live `Company`. All DAL/BLL data access reads `SapDiBase.Company` statically — there is no DI container. The DAL throws if `Company` is accessed before `SapUiBase` has initialized it.

### Where to add Add-On logic

Override the virtual methods in **`App.cs`** (already stubbed): `Loading`, `SBO_Application_ItemEvent`, `SBO_Application_MenuEvent`, `SBO_Application_FormDataEvent`, `SBO_Application_LayoutKeyEvent`. These map directly to SAP B1 UI API events registered in `SapUiBase.RegisterEvents()`. The existing stubs wrap bodies in try/catch and surface errors via `Application.SBO_Application.MessageBox` — follow that pattern.

Business logic goes in BLL services that extend `BaseService` and call into `SapDiBase` (see the documented `OrderService` example in `BaseService.cs`).

### Data access conventions

- **DI API queries** go through `SapDiBase.ExecQuery` / `GetScalarValue` (returns `SAPbobsCOM.Recordset`). Document CRUD via `GetDocument` / `UpdateDocument`. COM objects must be released with `SapDiBase.ReleaseObject`.
- **HANA vs SQL Server dialect** is handled in `QueryBuilder` (DAL) keyed off `SapDiBase.DbType`: HANA uses `"quoted"` identifiers and `CALL proc(name=>val)`; SQL Server uses `EXEC proc @name=val`. When writing raw SQL, account for both — HANA column/table names are double-quoted and case-sensitive (note the existing queries quote columns like `"USERID"`).
- SQL string values are escaped via `SqlHelper.EscapeString` (doubles single quotes) before interpolation — this template builds SQL by string concatenation, not parameterized commands.
- **Service Layer** (REST) is a separate path: `ServiceLayerClient` (DAL) — session-cookie auth, async CRUD over `/b1s/v1/...`, accepts self-signed certs. Independent of the DI API `Company`.

### Constants to update per Add-On

- `MenuConstants` — SAP menu UIDs (`540100`–`540104`); change to match your Add-On's menu registration.
- `SapConstants.DebugConnectionString` — the encoded UI API connection string used in debug mode (replace with the one from your local SAP B1 client). Also used by `GetProcessId` to map the SAP process.

## Conventions

- C# with nullable reference types and implicit usings enabled across all projects.
- The Presentation project aliases `SAPbouiCOM.Framework.Application` as `Application` (UI API) — distinct from the WinForms `Application`. Keep that alias in mind when adding `using`s.
- Static cross-layer state (`SapDiBase`, `SapUiBase` progress-bar fields) is the established pattern here; do not introduce a DI container without reason.
