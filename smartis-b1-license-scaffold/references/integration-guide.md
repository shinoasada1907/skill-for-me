# Integration guide — Smartis B1 License Layer Scaffold

Concrete wiring for the code bundled in `assets/`. All snippets are from the canonical VASManager
implementation (branch `vas_license`). Rename namespaces / base class / service layer to your project.

---

## 1. Load `AddOnVersionModel` from the `.ard` (do this FIRST, at startup)

`AddOnVersionModel` is the static identity every license query keys on. Populate it **once** during
initialization, **before** any `CheckLicense` or license query runs. Source: `InitialSetting.cs`.

```csharp
// Find the .ard next to the executable (walk up a few parent levels if needed).
// Then read its attributes:
XDocument xml = XDocument.Load(fileArd);
AddOnVersionModel.Version = xml.Root?.Attribute("ExtVersion")?.Value;
AddOnVersionModel.Name    = xml.Root?.Attribute("ExtName")?.Value;   // <-- license ADDON must equal this
AddOnVersionModel.Partner = xml.Root?.Attribute("Partner")?.Value;   // <-- PROVIDER, e.g. "SMARTIS"
```

> The `.ard` is an XML file in the parent directory of the executable. Rename the `*.ard` filename
> lookup to your add-on's `.ard`. If `AddOnVersionModel.Name` is null/empty, `CheckLicense` returns
> `"Addon name not initialized from .ard."` — so this step is mandatory.

---

## 2. Record-on-success init flow (no DELETE, auto-retry)

The shared `S0ADDONDATA_SP` has **no delete branch**; never delete data. Write the version row only
**after** init succeeds, so a failed install leaves no row and is retried at the next super-user login.

```csharp
// (a) read-only "is this a new version?" — mirrors the proc, no writes
string checkquery = AddonSqlQueries.DetectNewVersion(
    AddOnVersionModel.Name, AddOnVersionModel.Version, AddOnVersionModel.Partner);
recordset.DoQuery(checkquery);
int result = Convert.ToInt32(recordset.Fields.Item(0).Value);   // 0 = new (run init), 1 = installed

if (result == 0)
{
    if (!InitSetting())     return;   // create UDT/UDF/UDO (idempotent) — on fail, no row written
    if (!RegistingScript()) return;   // run DataScripts — on fail, no row written

    // (b) record the version ONLY now — calls the 4-arg S0ADDONDATA_SP (carries license forward)
    string execquery = AddonSqlQueries.ExecuteAddonDataProcedure(
        AddOnVersionModel.Name, AddOnVersionModel.Version, AddOnVersionModel.Partner,
        oCompany.UserSignature);
    SAPbobsCOM.Recordset rs = SapUiBase.ExecQuery(execquery);
    if (rs != null) SapUiBase.ReleaseObject(rs);
}
```

The `00.INIT` scripts (`00.S0SADC.sql`, `01.S0ADDONDATA_SP.sql`) must run before any license read.
Deploy `04.PROCEDURE/GetAddonInfo.sql` with your other procedures.

---

## 3. Enforcement wiring (per-form, at the menu)

`CheckLicense(formId, out msg)` is the centerpiece. Call it in `SBO_Application_MenuEvent` while
`pVal.BeforeAction`, so a licensed form only opens if the license covers it. `formId` is the form's
license object code (matched against the license `FUNCTIONS` table). The About/License menu is NOT
gated.

```csharp
private void OpenLicensedForm(string formId, string statusMessage, Action openForm)
{
    if (LicenseHelper.CheckLicense(formId, out string message))
    {
        Application.SBO_Application.SetStatusBarMessage(
            statusMessage, SAPbouiCOM.BoMessageTime.bmt_Short, false);
        openForm();
        return;
    }
    if (!string.IsNullOrEmpty(message))
        Application.SBO_Application.MessageBox(message);   // invalid/expired -> form not opened
}

// inside SBO_Application_MenuEvent:
if (pVal.BeforeAction)
{
    switch (pVal.MenuUID)
    {
        case Configration.ConfigMenuUid:
            OpenLicensedForm("CFGVAS", "Opening VAS Configuration...", () => new ConfigVAS().Show());
            break;
    }
}
```

> Forms the license marks `REQ=N` pass automatically (see `IsLicenseRequiredForFunction`). For a
> hard block at startup, also call `CheckLicense` during init. Common gap: `CheckLicense` defined
> but never called — confirm enforcement is actually wired.

---

## 4. Registration (the write side — Form is out of scope here)

This skill bundles the **query layer** support for registration but not the Form UI:

- `AddonSqlQueries.UpdateLicense(licenseKey, hashKey, fromDate, toDate, db, customer, name, ver, provider)`
  — UPDATEs the latest `S0SADC` row for this add-on with the license.
- `AddonSqlQueries.GetRegisteredLicense(name, provider)` — returns a row only if `INSTALLKEY IS NOT
  NULL`; use it for **Trial anti-reuse** (block re-registering a Trial).
- `LicenseHelper.ComputeSHA256Short(installKey)` — compute `SHKEY` over the **full** stored key.

The register orchestration (import file → validate imported license → resolve dates → compute SHKEY
→ `UpdateLicense`) lives in the License **Form**. For that, use the `smartis-b1-license-check` skill
(it documents the Form wiring, Trial anti-reuse, and the register-vs-enforce parse split).

---

## 5. Usage examples (display / validate)

```csharp
var licenseHelper = new LicenseHelper();

// Read + show full info in a message box
licenseHelper.ShowLicenseInfo();

// Validate file license (throws if expired / not-yet-valid / invalid)
var info = licenseHelper.ReadAndValidateLicense();

// Database coverage check
if (!info.HasDatabase(SapUiBase.oCompany.CompanyDB)) { /* block */ }

// Expiry warning (<= 30 days)
string warn = licenseHelper.GetExpiryWarning(30);
if (warn != null) Application.SBO_Application.SetStatusBarMessage(
    warn, SAPbouiCOM.BoMessageTime.bmt_Long, true);
```

`SmartisLicenseInfo` fields you'll use most: `ProductName`, `CustomerName`, `HardwareKey`
(`"Trial"` ⇒ trial), `InstallationNumber`, `FromDate`/`ToDate` (`yyyyMMdd`, `99991231` = perpetual),
`CompanySchemas`, `IsValid`/`IsExpired`/`IsPerpetual`/`DaysUntilExpiration`, plus `GetFunctions()` /
`GetDatabases()` / `GetUsers()` / `IsFunctionAccessible(formId)` / `HasDatabase(db)`.

---

## 6. Decode an LKEY for debugging

The stored `INSTALLKEY` may have a `:base64(start-end)` Trial-date suffix → `lkey.Split(':')[0]`
before parsing. The LKEY itself is AES-256-CBC, layout `[salt32][iv16][ciphertext]` Base64, key =
PBKDF2-SHA256(`AesLicenseEncryption.DefaultPassword`, salt, 10000) → 32 bytes. `ParseLicenseKeyToInfo`
without a password falls through to the default-password AES path. Source of truth for the crypto:
`SmartisLicenseLibrary` (`AesLicenseEncryption.cs`, `LicenseReader.cs`).

---

## 7. Dependency contract (quick reference)

| Bundled call | Provide in target project |
|---|---|
| `SapUiBase.oCompany` / `.DbServerType` / `.CompanyDB` / `.UserSignature` | DI/UI base class with the SAPbobsCOM.Company |
| `SapUiBase.ExecQuery(string)` → Recordset | base class query helper |
| `SapUiBase.ReleaseObject(obj)` | COM release helper |
| `SapUiBase.DecodeBase64(string)` | Base64 decode helper (Trial date window) |
| `new SDXServiceLayer().InstallationNumber()` | Service Layer client (official licenses only) |
| `LicenseReader`, `SmartisLicenseInfo`, `AesLicenseEncryption` | `SmartisLicenseLibrary.dll` |
| `Application.SBO_Application` | `SAPbouiCOM.Framework` |
| `AddOnVersionModel.Name/Version/Partner` | loaded from `.ard` at startup (section 1) |
