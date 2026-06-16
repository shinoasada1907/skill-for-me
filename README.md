# SAP Business One Add-On Skills for Claude Code

Two [Claude Code](https://claude.com/claude-code) skills that scaffold a new **SAP Business One (SAP B1) add-on** in seconds — type a project name, get a complete, **buildable** solution wired to attach to a running SAP B1 client.

## Skills

| Skill | What it does | Stack |
|---|---|---|
| **`sap-b1-addon-from-template`** *(recommended)* | Generate a new add-on from a bundled, layered project template (Presentation / BLL / DAL / Common, with UI API + DI API + Service Layer base classes). The template ships **inside** the skill, so it works on any machine with no external template folder. | .NET 10 (`net10.0-windows`, WinForms) |
| **`sap-b1-addon-scaffold`** | Generate a minimal, from-scratch hello-world add-on that connects via the UI API + DI API and shows a default "Hello" form. | .NET Framework 4.8 (x64), classic `.csproj` + COM references |

Both produce a solution that **builds out of the box**. With the SAP B1 client open and logged in, press **F5** and the add-on attaches.

## Install

Copy the skill folders into your personal Claude Code skills directory:

- **Windows:** `%USERPROFILE%\.claude\skills\`
- **macOS / Linux:** `~/.claude/skills/`

```powershell
git clone https://github.com/<your-user>/claude-sap-b1-addon-skills.git
Copy-Item .\claude-sap-b1-addon-skills\sap-b1-addon-* "$env:USERPROFILE\.claude\skills\" -Recurse
```

Restart Claude Code (skills load at session start).

## Use

In Claude Code, type `/sap` and pick a skill — or just ask in natural language:

> tạo addon SAP B1 tên WorkOrderAddon

Claude asks for the project name (the output folder defaults to the current directory), runs the generator, and builds it. Then:

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
- A running, logged-in SAP B1 client to actually F5/attach (not needed to generate or build).

## Updating the bundled template

`sap-b1-addon-from-template` carries its template in `assets/template/`. To update it, replace those files with your maintained template, **keeping the identifier token `TemplateAddOnDotNetCore` intact** in file names and contents — the generator replaces that token with the chosen add-on name.

## How it works

- **Template skill:** copies the bundled template (excluding `bin`/`obj`/`.vs`/`.git`), renames every folder and file containing the token, replaces the token inside every text file, verifies nothing is left over, then `dotnet build`.
- **From-scratch skill:** stamps out a single-project `.csproj` with `<COMReference>` to `SAPbouiCOM` / `SAPbobsCOM`, an STA `Program.cs`, event handling, a Modules menu item, a Hello form, and a pre-filled dev connection string so F5 connects.

## License

[MIT](LICENSE).
