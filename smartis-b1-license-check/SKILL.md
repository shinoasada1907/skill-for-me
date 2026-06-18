---
name: smartis-b1-license-check
description: >-
  Implement the Smartis SAP Business One (SAP B1) add-on license flow: read/import
  a SmartisLicenseLibrary license file, validate it (add-on name, company database,
  validity dates, SAP Installation Number, add-on identifier), persist it to the
  S0SADC table, and enforce it at runtime (block forms/menus when missing or
  expired). Covers both official and Trial licenses, including the Trial anti-reuse
  rule (one trial file cannot be registered twice) and the Trial date window encoded
  in the license key. Use this whenever the user wants to add, complete, review, or
  fix license registration / license checking / "kiểm tra license" / "đăng ký license"
  / "check DB license" / "khóa license" in a Smartis SAP B1 add-on (e-Invoice,
  Consolidation, VASManager, or a new add-on). Targets the SmartisLicenseLibrary.dll
  pattern shared across these projects; supports SQL Server + SAP HANA.
---

# Smartis SAP B1 License Check

Reusable pattern for the **license registration + validation + enforcement** flow
used across the Smartis SAP B1 add-ons. The canonical, most-mature implementation is
in `Consolidation` and `e-Invoice`; `VASManager` is the original. When implementing,
mirror the existing project's naming and helpers — do not invent new abstractions.

## Reference implementations (read these first when in doubt)

| Project | LicenseHelper | Form | Queries |
|---|---|---|---|
| Consolidation | `Consolidation\Consolidation\Helper\LicenseHelper.cs` | `Forms\LicenseForm.b1f.cs` | `Database\QueryStore.cs` |
| e-Invoice | `Smartis.EInvoice.B1Addon\Helper\LicenseHelper.cs` | `Forms\License.b1f.cs` | `Database\QueryStore.cs` |
| VASManager (origin) | `VASManager\Helpers\LicenseHelper.cs` | `Forms\Licenses.b1f.cs` | `AddonSqlQueries` |

Base paths: `D:\Code\Project\ProjectAddon\{Consolidation|VASAddon|e-Invoice}`.
Doc: `Consolidation\TASK_LICENSE_CHECK.md`, `VASAddon\...\README_LicenseHelper.md`.

## The flow (two entry points)

1. **Register** (License form): import a license file → validate in-memory → persist to `S0SADC`.
2. **Enforce** (`CheckLicense(formId)`): on startup / menu click / form open → load from `S0SADC`,
   verify integrity hash, validate, block if missing/expired/not-covered.

```
Register:  Import file → ReadLicenseFromFile → parse LKEY → (Trial) anti-reuse check
           → EnsureAddonInfoLoaded → ValidateImportedLicense → RegisterLicense (UPDATE S0SADC)
Enforce:   CheckLicense(formId) → TryLoadLicense (hash check) → ValidateAddonName
           → ValidateLicenseExpiry → REQ=N bypass → HasFunctionAccess
           → (official only) ValidateLicenseInfo (DB + InstallNo + Identifier)
```

## Required infrastructure

- **References**: `SmartisLicenseLibrary.dll`, `System.Security.Cryptography.ProtectedData.dll`,
  `System.Net.Http`, `Newtonsoft.Json`.
- **`S0SADC` table** (both `DataScripts\SQLSERVER` and `DataScripts\HANADB` — keep in sync, and
  note the real schema lives in `setup.json` base64 which runs FIRST at startup). Columns:
  `ID, NAME(20), VERSION(10), PROVIDER(10), TYPE(10), INSTCOMPNY(50), INSTALLDATE, INSTALLUSER(50),
  INSTALLKEY NVARCHAR(MAX)/NCLOB, SHKEY(50), STARTDATE, ENDDATE, CUSTOMERNAME(100)`.
  Startup inserts a row with `INSTALLKEY = NULL`; registration UPDATEs that row.
- **Addon-info store** (e.g. `S2InvGetAddonInfo`) returning `NAME, VERSION, PROVIDER`, filtered
  `INSTCOMPNY = DB_NAME()` and the add-on's `.ard` name. Used to populate `AddOnVersionModel`.
- **`AddOnVersionModel`** — static `Name / Version / Partner`. The identity all queries key on.
- **`QueryStore`** — dialect-aware (`IsHana`) builders: `GetLatestAddonLicense`,
  `GetRegisteredAddonLicense`, `UpdateAddonLicense`. Always escape strings (`'' `) and use
  ISO date literals `yyyy-MM-dd`.
- **ServiceLayer** with `InstallationNumber()` → calls `LicenseService_GetInstallationNumber`.
- **`Configuration.SapConnectionString`** — the hex connection string captured at startup
  (from `args[0]`), used by `VerifyAddonIdentifier` to test-connect via `SboGuiApi`.

## LicenseHelper — canonical API

- `ReadLicenseFromFile(path)` — `.smlc` → encrypted reader, else `ExtractLicenseInfo`.
- `ComputeSHA256Short(input, 16)` — integrity hash over the **full** stored key (incl. Trial suffix).
- `TryLoadLicense(out data, out licenseKey, out msg)` — **first call `EnsureAddonInfoLoaded()`**
  (enforcement can fire from a direct menu click before the License form ran, so `AddOnVersionModel`
  may be null → the query keys on nulls → "No license found"). Then read latest `S0SADC` row,
  recompute `SHKEY` and compare (integrity), `Split(':')`, then parse the key. **Always pass the AES
  password:** `ParseLicenseKeyToInfo(licenseKey[0], AesLicenseEncryption.DefaultPassword)` — see
  the AES-key gotcha below.
- `EnsureAddonInfoLoaded()` — populate `AddOnVersionModel` (Name/Version/Partner) from
  `S2InvGetAddonInfo` if empty. Make it a shared `LicenseHelper` method (not only a form method),
  so both the License form AND enforcement can call it.
- `CheckLicense(formId, out msg)` — full enforcement (cached per form per day).
- `ValidateImportedLicense(data, licenseKey[], out msg)` — register-time validation (in-memory).
- `RegisterLicense(data, installKey, out msg)` — `ResolveLicenseDates` → `SHKEY=ComputeSHA256Short(installKey)`
  → `QueryStore.UpdateAddonLicense(...)` → `ClearLicenseCheckCache()`.
- `ValidateAddonName` / `ValidateCompanyDatabase` / `ValidateLicenseInfo` / `ValidateLicenseExpiry`
  / `ResolveLicenseDates` / `VerifyAddonIdentifier`.

## The DB check (the core of "kiểm tra DB")

```csharp
// True when the license is not bound to any DB; otherwise current company DB must be listed.
public static bool ValidateCompanyDatabase(SmartisLicenseInfo data, out string message)
{
    message = string.Empty;
    if (data.CompanySchemas == null || data.CompanySchemas.Count == 0)
        return true;

    string currentDb = SapUiBase.oCompany.CompanyDB?.Trim() ?? string.Empty;
    if (string.IsNullOrEmpty(currentDb) || !data.HasDatabase(currentDb))
    {
        message = $"License mismatch: Company database '{currentDb}' is not licensed.\n" +
                  $"Licensed: {string.Join(", ", data.CompanySchemas)}";
        return false;
    }
    return true;
}
```
`CompanySchemas` comes from the license `DATABASES[].B1SCHEMA`. Compare against
`SapUiBase.oCompany.CompanyDB`. Use this method from BOTH official and Trial paths.

## Official vs Trial — what to check

- **Official**: add-on name + **DB** + dates + **Installation Number** (via Service Layer) +
  **Addon Identifier** (live `SboGuiApi` test-connect). SL down ⇒ block (security).
- **Trial**: add-on name + **DB** + dates + **anti-reuse only**. SKIP Installation Number and
  Identifier. In `ValidateImportedLicense`:
  ```csharp
  bool isTrial = string.Equals(data.HardwareKey, "Trial", StringComparison.OrdinalIgnoreCase);
  if (isTrial) { if (!ValidateCompanyDatabase(data, out message)) return false; }
  else         { if (!ValidateLicenseInfo(data, out message))    return false; }
  ```

### Trial date window
The real validity window is computed at **import** from "today" (`now → now + (ToDate-FromDate)`),
Base64-encoded, and appended to the key as `LKEY:base64window`. `ResolveLicenseDates` decodes
`licenseKey[1]` for Trial; official uses the embedded `FromDate/ToDate`.
**Always build/parse the window with a fixed `yyyyMMdd` format — never `DateTime.ToString()`
(culture-dependent: a locale using `-` as date separator breaks the later `Split('-')`).**

### Trial anti-reuse (one file, many registrations)
After a successful registration `INSTALLKEY` is non-null. Before registering a Trial, query
`GetRegisteredAddonLicense(name, partner)` (`INSTALLKEY IS NOT NULL`) — if a row exists, **block**
("Trial cannot be registered again"). This blocks re-importing the same/any Trial to reset the window.

## Register flow (License form) — wiring rules

- Map items, wire `Button.PressedAfter` in `OnInitializeComponent`; wire form load in
  `OnInitializeFormEvents` with `LoadAfter += new LoadAfterHandler(Form_LoadAfter);` so `ThisForm`
  is set before `OnCustomInitialize` → `SapUiBase.FormatForm(ThisForm)` (FormatForm NREs on null).
- **Call `EnsureAddonInfoLoaded()` in the Import handler BEFORE the Trial anti-reuse check** — it
  populates `AddOnVersionModel` (static, only loaded here). Otherwise `CurrentAddonPartner` throws
  "Addon provider is not available." and Trial import fails.
- Register handler: guard imported license → `EnsureAddonInfoLoaded` → `ValidateImportedLicense`
  → `RegisterLicense` → success message (consider closing the form after success).

## Enforcement wiring

Call `LicenseHelper.CheckLicense(formId, out msg)` and block when it returns false. The
`formId` is the form's **TypeEx** (e.g. `"SMCSUNIT"`), matched against the license FUNCTIONS
table — `CheckLicense` honors `REQ=N` (form not license-required) and caches per form per day.

**Per-form menu check (canonical pattern).** Wrap every licensed menu in one helper, called from
`SBO_Application_MenuEvent` while `pVal.BeforeAction` — so the form only opens if the license
covers it. The License/About menu itself is NOT gated.

```csharp
private void OpenLicensedForm(string formId, string statusMessage, Action openForm)
{
    if (LicenseHelper.CheckLicense(formId, out string message))
    {
        Application.SBO_Application.SetStatusBarMessage(statusMessage, SAPbouiCOM.BoMessageTime.bmt_Short, false);
        openForm();
        return;
    }
    if (!string.IsNullOrEmpty(message))
        Application.SBO_Application.MessageBox(message);   // invalid/expired -> form not opened
}

// inside SBO_Application_MenuEvent: if (pVal.BeforeAction) switch (pVal.MenuUID) { ... }
case Configuration.UnitMenuUid:
    OpenLicensedForm("SMCSUNIT", "Opening Consolidation Unit...", () => new ConUnit().Show());
    break;
```

Map every add-on menu UID that opens a business form to that form's TypeEx. Forms the license
marks `REQ=N` pass automatically. For a hard block at startup, also call `CheckLicense` in
`InitialSetting` / `GeneralSettings`.
> Common gap: `CheckLicense` is defined but never called — verify enforcement is actually wired.
> Note: enforcement re-parses the stored INSTALLKEY — make sure that parse passes the AES
> password (gotcha below), otherwise `CheckLicense` fails for every form.

## AES-key parse gotcha (read-back)

License files (`.txt`) usually carry an **AES-encrypted** LKEY (a long Base64 with `+/=`, NOT the
plain `eyJ...` Base64-JSON). `ExtractLicenseInfo` decrypts it automatically when importing, so
registration works. But the read-back paths (`TryLoadLicense` for enforcement, the About form)
re-parse the **stored** LKEY with `reader.ParseLicenseKeyToInfo(lkey)` — and that overload tries
plain Base64, then DPAPI, and only tries AES **if an `aesPassword` is given**. With no password an
AES key fails with *"Failed to parse license key to license info"*.

Fix every read-back call to pass the default password (safe for plain keys too — plain Base64 is
tried first and returns before AES):
```csharp
var data = reader.ParseLicenseKeyToInfo(licenseKey[0], AesLicenseEncryption.DefaultPassword);
```
`AesLicenseEncryption` / `DefaultPassword` are public in `SmartisLicenseLibrary`. Independently,
the About form should prefer the persisted `CUSTOMERNAME` + `ENDDATE` columns and treat the key
parse as best-effort, so the display never depends on the parse succeeding. Persist `CUSTOMERNAME`
at registration (`UpdateAddonLicense`) or the About line shows "Non License".

**Register-vs-enforce parse mismatch (important).** Registration validates the `SmartisLicenseInfo`
from `ReadLicenseFromFile`→`ExtractLicenseInfo` (a FILE parse). For an AES-encrypted LKEY that path
decrypts via **DPAPI**, fails, and leaves `LicenseData = null` → so `data.Addon` is **empty** and
`ValidateAddonName` (guarded by `if (!IsNullOrEmpty(data.Addon))`) is a **no-op** → a wrong-addon
license registers "successfully", then enforcement (which parses the key with the AES password and
DOES see `Addon`) blocks every form. Fix: in `ValidateImportedLicense`, when `data.Addon` is empty,
re-parse `licenseKey[0]` with `AesLicenseEncryption.DefaultPassword` and validate the name against
that — so register rejects the wrong addon up front, consistent with enforcement. (The same
file-vs-key split means register reads DB/InstallNo/Identifier from the file headers while enforce
reads them from the LKEY JSON — keep the license file headers and LKEY contents in sync.)

## Gotchas / checklist

- [ ] `S0SADC` schema updated in **both** SQLSERVER + HANADB scripts **and** `setup.json` base64.
- [ ] `INSTALLKEY` is `NVARCHAR(MAX)` / `NCLOB`; `SHKEY` fits 16-hex.
- [ ] When reading `INSTALLKEY` back, **`CAST(INSTALLKEY AS NVARCHAR(4000))`** in the SELECT — the
      SAP DI API Recordset truncates an NVARCHAR(MAX) column on read (regardless of column position,
      so reordering does NOT fix it), giving `hash(read) != SHKEY` → "Hash mismatch" in enforcement
      and a failed license parse. Casting to a bounded type makes DI read the full value. 4000 fits
      any license key. (Note: the About form can still look OK via the CUSTOMERNAME/ENDDATE columns
      even when its key parse silently fails — don't treat a correct About line as proof the key read
      is intact.)
- [ ] Addon-info store filters `INSTCOMPNY = DB_NAME()` and matches the `.ard` add-on name.
- [ ] `AddOnVersionModel` populated before any query that keys on it — `TryLoadLicense` must call
      `EnsureAddonInfoLoaded()` itself (menu-click enforcement runs before the License form).
- [ ] Trial dates use fixed `yyyyMMdd` (culture-safe).
- [ ] `SHKEY` computed over the **full** `installKey` on both write (RegisterLicense) and read (TryLoadLicense).
- [ ] Don't leak provider/customer secrets; the SboGuiApi connection string stays the captured hex.
