---
name: smartis-b1-license-scaffold
description: >-
  Quickly add / scaffold the Smartis SAP Business One (SAP B1) add-on LICENSE LAYER into a new
  or existing add-on by copying ready-to-use canonical code BUNDLED inside this skill (extracted
  from the VASManager project, branch vas_license): the `LicenseHelper` class (read license file,
  parse LKEY, CheckLicense enforcement, expiry/DB/installation-number/trial checks), the
  dialect-aware `AddonSqlQueries` builder, the `AddOnVersionModel` identity, and the SQL for the
  shared `S0SADC` table + `S0ADDONDATA_SP` proc + `GetAddonInfo` proc (SQL Server + HANA). Use
  whenever the user wants to "implement / scaffold / bundle / copy the license code", "add a
  LicenseHelper class", "dựng/thêm phần license cho addon", "tạo class LicenseHelper", "copy luồng
  license sang addon khác", "implement license nhanh cho dự án khác" — i.e. they want the actual
  code dropped in, not just guidance. COMPLEMENTS: `smartis-b1-license-check` (guidance + the
  registration FORM) and `smartis-s0sadc-standardize` (align the shared S0SADC across add-ons).
  Supports SQL Server + SAP HANA. Targets the SmartisLicenseLibrary.dll pattern.
---

# Smartis SAP B1 — License Layer Scaffold

This skill **bundles the actual license code** (canonical = `VASManager`, branch `vas_license`) so
you can drop it into another add-on without re-deriving anything. Scope = **Helper + Query + SQL**
(no registration Form UI — for that, see `smartis-b1-license-check`).

## When to use this vs the sibling skills

| Skill | Use when |
|---|---|
| **`smartis-b1-license-scaffold`** (this) | You want the **real code** (LicenseHelper class, query builder, S0SADC/proc SQL) copied into a project, fast. |
| `smartis-b1-license-check` | You want **guidance** to add/review/fix the flow in place, or you need the **registration Form** (import file → validate → write S0SADC). |
| `smartis-s0sadc-standardize` | You need the shared **`S0SADC` table + `S0ADDONDATA_SP` proc** identical across all add-ons on one DB. |

## What's bundled (`assets/`)

```
assets/
  Helpers/
    LicenseHelper.cs        # read file, parse LKEY, CheckLicense(formId) enforcement, all validations
    AddonSqlQueries.cs      # dialect-aware (SQL Server / HANA) query builder for S0SADC + procs
  Models/
    AddOnVersionModel.cs    # static Name/Version/Partner — the identity every query keys on
  DataScripts/
    SQLSERVER/00.INIT/00.S0SADC.sql            # idempotent, non-destructive table migration
    SQLSERVER/00.INIT/01.S0ADDONDATA_SP.sql    # canonical 4-param proc + license carry-forward
    SQLSERVER/04.PROCEDURE/04.00.GetAddonInfo.sql  # VASGetAddonInfo (CAST INSTALLKEY)
    HANADB/00.INIT/00.S0SADC.sql
    HANADB/00.INIT/01.S0ADDONDATA_SP.sql
    HANADB/04.PROCEDURE/04.00.GetAddonInfo.sql
```

Assets are **verbatim from VASManager**. Note: the SBO connection string hardcoded in
`LicenseHelper.ValidateAddonIdentifier` is a fixed constant (the same value on every install) and is
kept as-is — the only per-server value is the license `IdentifierKey`, which already comes from the
license at runtime. Nothing to change there.

## Prerequisites in the target project (the dependency contract)

The bundled code calls into infrastructure that the target add-on must already provide:

- **`SmartisLicenseLibrary.dll`** referenced — provides `LicenseReader`, `SmartisLicenseInfo`,
  `AesLicenseEncryption`.
- **A UI/DI base class** exposing the statics used by the helper/queries — in VASManager this is
  `UIAPI.SapUiBase`: `oCompany` (SAPbobsCOM.Company), `ExecQuery(string) → Recordset`,
  `ReleaseObject(object)`, `DecodeBase64(string) → string`, and `oCompany.DbServerType`.
- **A Service Layer client** with `InstallationNumber() → string` — in VASManager
  `SDXManager.Service_Layer.SDXServiceLayer`. Used ONLY for official (non-Trial) licenses.
- **`SAPbouiCOM.Framework.Application`** (status bar / message box).
- **An `.ard` file** + a loader that fills `AddOnVersionModel.Name/Version/Partner` from the
  `ExtName/ExtVersion/Partner` attributes at startup (snippet in `references/integration-guide.md`).
- **An init runner** that executes the `DataScripts/.../00.INIT` scripts before any license read,
  and deploys `04.PROCEDURE/GetAddonInfo.sql`.

## How to use

### Option A — scripted (fast start)
Run the generator; it copies `assets/` into your project and rewrites the root namespace:
```powershell
& "<skill-dir>\scripts\New-SmartisLicenseFlow.ps1" `
    -TargetProjectDir "D:\Code\Project\ProjectAddon\<YourAddon>\<YourAddon>" `
    -RootNamespace "YourAddon"
```
Then finish the **adaptation checklist** below (the script cannot guess your base class / service
layer / proc name). It never builds or runs anything.

### Option B — manual
Copy `assets/Helpers`, `assets/Models`, `assets/DataScripts` into the project, then apply the checklist.

## Adaptation checklist (do every item)

- [ ] **Namespaces** `VASManager.Helpers`, `VASManager.Models` → your root namespace (the script does this).
- [ ] **Base class**: `using UIAPI;` + every `SapUiBase.*` → your DI/UI base class with the same statics.
- [ ] **Service layer**: `using SDXManager.Service_Layer;` + `new SDXServiceLayer()` → your SL client
      exposing `InstallationNumber()`.
- [ ] **AddonIdentifier connection string** in `LicenseHelper.ValidateAddonIdentifier`: **no change** —
      it's a fixed constant (same on every install), kept hardcoded on purpose. The per-server value
      is the license `IdentifierKey`, already read from the license at runtime.
- [ ] **`.ard` loader**: populate `AddOnVersionModel` from your add-on's `.ard` (rename the `*.ard`
      filename). License `ADDON` must equal `.ard` `ExtName` (OrdinalIgnoreCase).
- [ ] **GetAddonInfo proc name** `VASGetAddonInfo` (in `AddonSqlQueries.GetAddonInfo` and both proc
      files) → keep, or rename consistently in all three places.
- [ ] **Provider / Partner** default is `'SMARTIS'`; **license file** default is `license.txt`.
- [ ] **Shared DB objects**: `S0SADC` + `S0ADDONDATA_SP` are shared by ALL add-ons on one company DB
      — the body must be IDENTICAL everywhere. If others already define them, run
      `smartis-s0sadc-standardize` instead of blindly overwriting.

## The flow (what the bundled code does)

```
Enforce (this skill's core):
  CheckLicense(formId)                         [LicenseHelper, static]
    -> AddonSqlQueries.GetLatestLicense(name,ver,partner)  (CAST INSTALLKEY!)
    -> read SHKEY + INSTALLKEY from S0SADC
    -> Split(':'), reader.ParseLicenseKeyToInfo(key[0])
    -> addon name == AddOnVersionModel.Name (.ard)
    -> integrity: ComputeSHA256Short(full lkey) == SHKEY
    -> dates: Trial = DecodeBase64(key[1]) "start-end"; official = FDATE/TDATE (yyyyMMdd)
    -> function gating: REQ=N bypass, else IsFunctionAccessible(formId)
    -> official only: HasDatabase + InstallationNumber + AddonIdentifier
Register (NOT bundled — see smartis-b1-license-check for the Form):
    import file -> validate -> ComputeSHA256Short -> AddonSqlQueries.UpdateLicense(...)
    (query layer here already provides UpdateLicense + GetRegisteredLicense for anti-reuse)
```

## Gotchas (carried over — keep them)

1. **CAST `INSTALLKEY` AS NVARCHAR(4000)** on EVERY read. The SAP DI API `Recordset` truncates a
   `NVARCHAR(MAX)`/`NCLOB` column → `hash(read) != SHKEY` and the key parse fails. Already applied
   in `GetLatestLicense` and the `GetAddonInfo` procs — apply to any new read path too.
2. **Addon name from `.ard`, not hardcoded.** The decrypted LKEY `ADDON` (e.g. `VASManagerAddon`)
   must match `.ard` `ExtName` — that's why the model is loaded from the `.ard`.
3. **SHKEY integrity** is computed over the **full** stored `INSTALLKEY` (including any `:trial`
   suffix) on both write and read.
4. **Trial date window**: stored as `LKEY:base64(start-end)`; split on `:`, `DecodeBase64` part[1].
   Official dates come from `FDATE/TDATE` parsed with fixed `yyyyMMdd` (culture-safe).
5. **Shared & identical**: `S0SADC` + `S0ADDONDATA_SP` — whichever add-on loads last re-creates the
   proc, so a divergent copy breaks the others. The bundled proc carries the license forward to a
   new version row (so version bumps don't blank the license).
6. **AES LKEY parse**: VASManager calls `reader.ParseLicenseKeyToInfo(key[0])` WITHOUT a password
   and relies on the library's auto-parse falling through to the default AES password. If your
   `SmartisLicenseLibrary` build's no-password overload does NOT auto-try AES, pass
   `AesLicenseEncryption.DefaultPassword` explicitly (see `smartis-b1-license-check`).
7. **Trial vs official**: Trial skips InstallationNumber + AddonIdentifier (keeps name + dates +
   function gating); official checks DB + InstallationNumber + AddonIdentifier.

## Verify (do NOT build/run for the user — Smartis project rule)

- After copying: namespaces resolve, no leftover `VASManager.*` references, braces balanced.
- The user builds & deploys, then confirms: init log clean, `CheckLicense` blocks/permits correctly,
  About/expiry display works.
- DB: `S0SADC` has 13 columns incl. `INSTALLUSER` + `INSTALLKEY` MAX/NCLOB; `S0ADDONDATA_SP` has 4
  params; `VASGetAddonInfo` exists.

See `references/integration-guide.md` for the `.ard` loader, record-on-success init, enforcement
wiring, and usage examples.
