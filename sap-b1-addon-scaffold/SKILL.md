---
name: sap-b1-addon-scaffold
description: >-
  Scaffold a ready-to-run SAP Business One (SAP B1) add-on project in C# / .NET
  Framework that connects to the running SAP client via the UI API + DI API and
  shows a default "Hello" form. Generates the full Visual Studio solution — a
  .csproj with the correct SAPbouiCOM / SAPbobsCOM COM references, Program.cs,
  event handling, a menu item under Modules, the Hello form, and a pre-filled
  development connection string so pressing F5 just connects and shows the form.
  Use this ONLY when the user explicitly wants a bare-bones .NET Framework
  hello-world add-on generated from scratch with NO project template (e.g. "minimal
  SAP B1 addon from scratch", "simple .NET Framework B1 hello addon", "addon B1 tối
  giản không dùng template"). For the normal case — starting a new add-on from the
  standard layered .NET 10 team template — use sap-b1-addon-from-template instead.
  Targets SAP B1 10.0 (x64) by default; supports 9.3 (x86).
---

# SAP Business One Add-On Scaffolder

Generate a complete, **buildable** SAP B1 add-on skeleton. When run, the add-on
attaches to a running SAP Business One client (UI API), optionally grabs a DI API
`Company`, adds a menu item under **Modules**, and shows a "Hello" form.

The heavy lifting is done by a deterministic generator script, so every run
produces a consistent, correct project. Your job is to collect a couple of inputs,
run the script, build it as proof, and tell the user how to F5.

## What gets generated

```
<Name>/
  <Name>.sln                 Visual Studio solution
  <Name>.csproj              classic .NET Framework project, COMReference to SAPbouiCOM/SAPbobsCOM
  <Name>.csproj.user         dev connection string pre-filled (Debug > Application arguments)
  Program.cs                 [STAThread] Main -> new Addon(args); Application.Run()
  Addon.cs                   connect UI (+DI), event filters, menu item, Hello form (XML + code)
  HelloForm.srf              XML form definition (only when FormStyle=xml)
  Properties/AssemblyInfo.cs
  App.config
  README.md                  run/debug + troubleshooting for the generated project
```

## Quick start

The generator is `scripts/New-SapB1Addon.ps1` (in this skill's folder). Run it with
PowerShell. The only required inputs are **`-Name`** and **`-OutputDir`**:

```powershell
& "<skill-dir>/scripts/New-SapB1Addon.ps1" -Name MyAddon -OutputDir "D:\Code\Addons" -Build
```

That creates `D:\Code\Addons\MyAddon\`, and with `-Build` compiles it immediately as
proof that the COM references resolve and the code is valid.

## How to use this skill

1. **Get the two required inputs.** Ask the user for:
   - **Add-on name** (`-Name`) — letters/digits/underscore, starts with a letter
     (e.g. `WorkOrderTools`). Used for the assembly, project, and namespace.
   - **Where to create it** (`-OutputDir`) — the parent folder; the project lands in
     `<OutputDir>\<Name>`. If they don't say, suggest a sensible path and confirm —
     don't dump a project in a random location.

2. **Apply defaults unless the user says otherwise** (these are the project owner's
   confirmed preferences):
   - `-Version 10.0` → `-Platform x64`
   - `-Connect UIDI` (UI API + DI API)
   - `-FormStyle xml` (Hello form from `HelloForm.srf`)
   Only ask about these if the user signals a different target (e.g. mentions 9.3, a
   32-bit client, "UI only / no DI", or "build the form in code").

3. **Run the generator** with `-Build` so the user gets immediate proof it compiles.
   Pass `-Force` only if regenerating over an existing folder.

4. **Report the result**, then give the run instructions (step 5 below). If `-Build`
   succeeded, say so and point at `bin\Debug\<Name>.exe`. If MSBuild wasn't found, tell
   them to open the `.sln` in Visual Studio and build there.

5. **Tell the user how to run it** (this is what "it just works" means):
   - Open `<Name>.sln` in Visual Studio.
   - Start the SAP Business One client and **log into a company** on this machine.
   - Press **F5**. The dev connection string is already in `<Name>.csproj.user`, so the
     add-on attaches to the running client, adds the *Modules* menu item, and shows the
     Hello form.

## Parameters

| Parameter | Default | Notes |
|---|---|---|
| `-Name` (required) | — | Assembly / project / namespace name. |
| `-OutputDir` (required) | — | Parent folder; project created in `<OutputDir>\<Name>`. |
| `-Namespace` | = `-Name` | Override the root namespace. |
| `-Version` | `10.0` | `10.0` or `9.3`. Drives interop type-lib version + default bitness. |
| `-Platform` | `x64` (10.0) / `x86` (9.3) | Must match the client bitness. **Never AnyCPU.** |
| `-Connect` | `UIDI` | `UIDI` = UI API + DI API `Company`; `UI` = UI API only. |
| `-FormStyle` | `xml` | `xml` = form from `HelloForm.srf`; `code` = form built in C#. |
| `-Build` | off | Compile with MSBuild after generating (recommended for proof). |
| `-Force` | off | Overwrite an existing target folder. |

## Environment assumptions

This generates a **classic .NET Framework** project (COM interop), so it needs a
Windows dev box with:
- **SAP Business One SDK** installed (registers the `SAPbouiCOM` / `SAPbobsCOM` type
  libraries — MSBuild's `tlbimp` generates the interop assemblies at build time; that's
  why no `Interop.*.dll` files are shipped).
- **Visual Studio with MSBuild** (the script locates it via `vswhere`, then known VS
  install paths). .NET Framework **4.8** targeting pack.
- A **running SAP B1 client logged into a company** to actually F5/attach (not needed
  just to generate or compile).

If MSBuild or the SDK is missing, generation still works — only `-Build` and runtime
attach are affected.

## After scaffolding — extending the add-on

Point the user at `references/sap-b1-addon-reference.md` (and the generated `README.md`)
when they want to go further. Read that reference yourself before:
- adding fields/buttons and handling their `ItemEvent`s,
- reading/writing business objects through the DI `Company`,
- packaging for deployment (the `.ard` file via `AddOnRegDataGen.exe` and Add-On
  Administration),
- or debugging connection / bitness / event problems (it has a pitfalls table).

## Important details (so the generated code makes sense)

- **Bitness must match the client.** 10.0 client is 64-bit → build x64; 9.3 is 32-bit →
  x86. AnyCPU silently resolves to 64-bit and fails against a 32-bit client.
- **`Embed Interop Types = False`** on both COM references — embedding ("no-PIA") breaks
  the COM event interfaces and casting `item.Specific`. The generated csproj sets this.
- **STA + message loop.** `Main` is `[STAThread]` and ends in `Application.Run()`; without
  the message pump the add-on connects then exits and no events fire.
- **Connection string.** `args[0]` is the connection string — SAP supplies the real one
  for a registered add-on; for debugging the dev string lives in `<Name>.csproj.user`.
- **DI without a second login.** `oCompany = (SAPbobsCOM.Company)SBO_Application.Company.GetDICompany();`
  reuses the user's existing UI session.

## Verifying

You can prove the scaffold compiles (COM references resolve, C# is valid) by running with
`-Build`. You **cannot** verify the runtime connect + form display without a live SAP
client and login — be honest about that and ask the user to do the final F5 check.
