---
name: sap-b1-addon-from-template
description: >-
  Create a new SAP Business One (SAP B1) add-on from the .NET 10 project template
  bundled inside this skill (TemplateAddOnDotNetCore — a layered Presentation / BLL /
  DAL / Common solution with UI API + DI API + Service Layer base classes), renamed to
  a project name the user chooses. The template ships INSIDE the skill, so it works on
  any machine with no external template folder. This is the DEFAULT way to start a new
  SAP B1 add-on / B1 add-on / Business One SDK project in this environment. Use it
  whenever the user wants to create, scaffold, bootstrap, start, or "set up a new" SAP
  B1 add-on project — or asks in Vietnamese, e.g. "tạo project addon SAP B1", "tạo
  addon SAP B1 mới", "tạo addon theo template", "dựng addon B1", "tạo addon B1 net
  core", "tạo dự án addon B1" — even if they don't name this skill or the template. If
  the user invokes it without a name, ASK them for the project name, then generate.
  Produces a .NET 10 (net10.0-windows / WinForms) solution ready to open and F5. For a
  minimal from-scratch .NET Framework hello-world add-on, use sap-b1-addon-scaffold.
---

# New SAP B1 Add-On from Template

Generate a new SAP Business One add-on from the project template **bundled inside this
skill**. The user picks a name; the skill stamps out a complete, renamed solution that
builds out of the box. Because the template travels with the skill, this works on any
machine — it does **not** depend on any external template folder.

## The template (bundled)

- **Lives in this skill** at `assets/template/` (a copy of the team template
  `TemplateAddOnDotNetCore`). Self-contained: no external folder required.
- **Stack:** .NET 10 (`net10.0-windows`, WinForms), references `SAPBusinessOneSDK.dll`
  (UI API `SAPbouiCOM` + DI API `SAPbobsCOM`) and includes a Service Layer REST client.
- **Architecture:** 4 projects, one-directional Presentation → BLL → DAL → Common.
- **Token:** the single identifier `TemplateAddOnDotNetCore` is used for the solution,
  every project, folder, namespace and project reference — renaming = replacing it.

The new solution becomes: `<Name>.slnx` + `<Name>` (Presentation), `<Name>.BLL`,
`<Name>.DAL`, `<Name>.Common` — every file, folder and namespace carries the new name
(nothing keeps the template name).

## How to use this skill

1. **Ask for the project name if it wasn't given.** When invoked via the skill picker
   (e.g. `/sap-b1-addon-from-template`) with no details, ask the user: *"Tên project
   addon là gì?"* (and where to create it). Then:
   - **`-Name`** — the add-on name; a valid C# identifier (e.g. `WorkOrderAddon`). Becomes
     the solution, all four project names, folders and root namespaces.
   - **`-OutputDir`** — parent folder; the solution lands in `<OutputDir>\<Name>`. Defaults
     to the current working directory if not given — tell the user where it will be created.

2. **Run the generator** (in this skill's `scripts/` folder) with `-Build` for immediate
   proof it compiles. Do NOT pass `-TemplatePath` — it defaults to the bundled template:

   ```powershell
   & "<skill-dir>/scripts/New-SapB1AddonFromTemplate.ps1" -Name WorkOrderAddon -OutputDir "D:\Code\Addons" -Build
   ```

   The script copies the bundled template, renames every token-bearing folder and file,
   replaces the token inside every text file, verifies no residual token remains, and runs
   `dotnet build <Name>.slnx`.

3. **Report the result.** If the build succeeded, say so and point at the `.slnx`. The
   script also warns if any residual `TemplateAddOnDotNetCore` is left (should be none) —
   surface that if it appears.

4. **Tell the user the per-add-on customizations** (the template marks these as TODO):
   - `<Name>.Common\Constants\MenuConstants.cs` — menu UIDs (default `540100`–`540104`);
     change so they don't clash with other add-ons.
   - `<Name>.Common\Constants\SapConstants.cs` — `DebugConnectionString` (the encoded UI
     API string for F5 debugging; the template ships the standard same-machine dev string).
   - Add logic by overriding the stubbed methods in `App.cs` (`Loading`,
     `SBO_Application_*Event`). Business logic goes in BLL services extending `BaseService`.

5. **How to run it:** open `<Name>.slnx` in Visual Studio, start the SAP B1 client and log
   into a company, then **F5** to attach (it cannot meaningfully run without a live client).

## Parameters

| Parameter | Default | Notes |
|---|---|---|
| `-Name` (required) | — | New add-on / solution / namespace name (C# identifier; dots allowed). |
| `-OutputDir` | current directory | Parent folder; solution created in `<OutputDir>\<Name>`. |
| `-TemplatePath` | bundled `assets/template` | Defaults to the template inside the skill. Override only to use a different template copy. |
| `-Token` | `TemplateAddOnDotNetCore` | The identifier replaced by `-Name`. Change only if a custom template's token differs. |
| `-Build` | off | Run `dotnet build <Name>.slnx` after generating (recommended). |
| `-Force` | off | Overwrite an existing target folder. |

## Environment assumptions

- The **template itself is bundled** in the skill — no external template folder needed.
- **.NET 10 SDK** (the template targets `net10.0-windows`; `dotnet build` of the `.slnx`
  needs the .NET 9+/10 CLI).
- **SAP B1 10.0 SDK** present so `SAPBusinessOneSDK.dll` resolves at the template's
  `HintPath` (`C:\Program Files (x86)\SAP\SAP Business One SDK\Lib\SAPBusinessOneSDK.dll`).
  If the build fails on missing SAP types, fix that `HintPath` in the `.csproj` files —
  don't remove the reference.
- A **running SAP B1 client logged into a company** to actually F5/attach (not needed to
  generate or build).

## Notes

- The `.slnx` solution format references projects by path (no GUIDs), so the result is a
  clean copy+rename with nothing to re-key. Nothing retains the template name.
- If `dotnet` isn't found, generation still works; the user can build by opening the
  `.slnx` in Visual Studio.
- Updating the bundled template later: re-copy your maintained template into the skill's
  `assets/template/` (keep the `TemplateAddOnDotNetCore` token intact in names + contents).
- Verifying: `-Build` proves the generated solution compiles. The runtime connect + UI can
  only be confirmed against a live SAP client — ask the user to do the final F5 check.
