# SAP Business One Add-On Skills for Claude Code

[Claude Code](https://claude.com/claude-code) skills for building **SAP Business One (SAP B1) add-ons** — scaffold a new add-on from a project name, or implement common add-on flows (such as license registration & checking, or standardizing shared DB objects) following an established team pattern.

## Skills

| Skill | What it does | Stack |
|---|---|---|
| **`sap-b1-addon-from-template`** *(recommended)* | Generate a new add-on from a bundled, layered project template (Presentation / BLL / DAL / Common, with UI API + DI API + Service Layer base classes). The template ships **inside** the skill, so it works on any machine with no external template folder. | .NET 10 (`net10.0-windows`, WinForms) |
| **`sap-b1-addon-scaffold`** | Generate a minimal, from-scratch hello-world add-on that connects via the UI API + DI API and shows a default "Hello" form. | .NET Framework 4.8 (x64), classic `.csproj` + COM references |
| **`smartis-b1-license-check`** | Implement, review, or fix the Smartis license flow in an **existing** add-on: import & validate a `SmartisLicenseLibrary` license file (add-on name, company DB, validity dates, SAP Installation Number, add-on identifier), persist it to the `S0SADC` table, and enforce it at runtime. Covers official + Trial licenses (with the one-file-one-registration anti-reuse rule). | C# pattern (no codegen), SQL Server + SAP HANA |
| **`smartis-s0sadc-standardize`** | Standardize the **shared** `S0SADC` table + `S0ADDONDATA_SP` procedure to one canonical schema across all Smartis add-ons on the same company DB. Fixes the "Invalid column name 'INSTALLGUSER'" / "too many arguments" init errors via a non-destructive migration (rename/add columns, widen `INSTALLKEY`), aligns the proc to **4 parameters**, and switches init to "record-on-success". | C# + SQL pattern, SQL Server + SAP HANA |

The two scaffolders produce a solution that **builds out of the box** — with the SAP B1 client open and logged in, press **F5** and the add-on attaches. `smartis-b1-license-check` is a pattern/reference skill: it guides Claude to wire the license flow into an existing add-on using that project's own helpers.

## Install

Copy the skill folders into your personal Claude Code skills directory:

- **Windows:** `%USERPROFILE%\.claude\skills\`
- **macOS / Linux:** `~/.claude/skills/`

```powershell
git clone https://github.com/shinoasada1907/skill-for-me.git
Copy-Item .\skill-for-me\sap-b1-addon-*, .\skill-for-me\smartis-* "$env:USERPROFILE\.claude\skills\" -Recurse
```

Restart Claude Code (skills load at session start).

## Use

In Claude Code, type `/sap` and pick a skill — or just ask in natural language:

> tạo addon SAP B1 tên WorkOrderAddon

Claude asks for the project name (the output folder defaults to the current directory), runs the generator, and builds it. Then:

For the license skill, just describe the task in an existing add-on:

> thêm / kiểm tra / fix luồng đăng ký license cho addon này

Claude reads the project's existing helpers and reference implementations, then wires the import → validate (add-on name, company DB, dates, Installation Number, identifier) → persist `S0SADC` → enforce flow. No code is generated standalone; it edits the add-on in place.

For standardizing the shared registration table across add-ons, describe it in an existing add-on:

> chuẩn hóa S0SADC và store S0ADDONDATA_SP cho addon này

Claude rewrites the `S0SADC` table script (idempotent, non-destructive) and `S0ADDONDATA_SP` to the canonical schema for both SQL Server + HANA, fixes the C# query/init layer, and lists the verification steps.

For the scaffolders:

1. Open `<Name>.slnx` (or `<Name>.sln`) in Visual Studio.
2. Start the SAP B1 client and log into a company.
3. Press **F5** — the add-on attaches and shows its form.

You can also run the generators directly:

```powershell
# Template-based (.NET 10) — recommended
& ".\sap-b1-addon-from-template\scripts\New-SapB1AddonFromTemplate.ps1" -Name WorkOrderAddon -OutputDir D:\Code\Addons -Build

# From scratch (.NET Framework)
& ".\sap-b1-addon-scaffold\scripts\New-SapB1Addon.ps1" -Name HelloAddon -OutputDir D:\Code\Addons -Build
```

Only `-Name` is required; `-OutputDir` defaults to the current directory.

## Requirements

- **Windows** with **Visual Studio / MSBuild**.
- **SAP Business One 10.0 SDK** installed — provides the `SAPbouiCOM` / `SAPbobsCOM` type libraries and `SAPBusinessOneSDK.dll`. If a build fails on missing SAP types, fix the reference (`HintPath` / COM reference) for your install — don't remove it.
- `sap-b1-addon-from-template` → **.NET 10 SDK**.
- `sap-b1-addon-scaffold` → **.NET Framework 4.8** targeting pack.
- `smartis-b1-license-check` → an **existing** add-on that references **`SmartisLicenseLibrary.dll`** (plus `System.Security.Cryptography.ProtectedData.dll`, `System.Net.Http`, `Newtonsoft.Json`) and has the `S0SADC` table + an addon-info store. It does not scaffold a project — it implements the license flow inside one you already have.
- `smartis-s0sadc-standardize` → an **existing** Smartis add-on with the `S0SADC` table + `S0ADDONDATA_SP` proc scripts (DataScripts SQL and/or `setup.json`) and a C# query/init layer. Pattern/reference skill — no codegen.
- A running, logged-in SAP B1 client to actually F5/attach (not needed to generate or build).

## Updating the bundled template

`sap-b1-addon-from-template` carries its template in `assets/template/`. To update it, replace those files with your maintained template, **keeping the identifier token `TemplateAddOnDotNetCore` intact** in file names and contents — the generator replaces that token with the chosen add-on name.

## How it works

- **Template skill:** copies the bundled template (excluding `bin`/`obj`/`.vs`/`.git`), renames every folder and file containing the token, replaces the token inside every text file, verifies nothing is left over, then `dotnet build`.
- **From-scratch skill:** stamps out a single-project `.csproj` with `<COMReference>` to `SAPbouiCOM` / `SAPbobsCOM`, an STA `Program.cs`, event handling, a Modules menu item, a Hello form, and a pre-filled dev connection string so F5 connects.
- **License skill:** a pattern/reference skill (no generator). Its `SKILL.md` captures the canonical license flow — required infrastructure (`S0SADC`, `AddOnVersionModel`, `QueryStore`, Service Layer, connection string), the `LicenseHelper` API, the company-DB check, official-vs-Trial rules, register/enforcement wiring, and a checklist of known pitfalls — pointing at the reference implementations in the e-Invoice, Consolidation, and VASManager add-ons.
- **S0SADC standardize skill:** a pattern/reference skill carrying the canonical shared `S0SADC` schema (13 columns, `INSTALLUSER`, `INSTALLKEY` MAX/NCLOB) and the canonical 4-parameter `S0ADDONDATA_SP`, plus the non-destructive migration (SQL Server + HANA), the C# query/init fixes, and verification queries — for keeping every Smartis add-on in sync on the shared registration table.

## License

[MIT](LICENSE).
