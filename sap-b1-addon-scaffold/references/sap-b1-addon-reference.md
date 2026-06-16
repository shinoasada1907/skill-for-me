# SAP B1 Add-On — Deep Reference

Companion to the `sap-b1-addon-scaffold` skill. Read this when extending the generated
skeleton (events, DB access, deployment) or debugging connect/bitness/event issues.

> **Verified on this toolchain:** the scaffold builds clean for **SAP B1 10.0 / x64 /
> .NET Framework 4.8** using `<COMReference>` to the registered type libraries
> (`SAPbouiCOM {6048236A-956D-498D-A6F1-9C81C13AB6E8}`,
> `SAPbobsCOM {FC8030BE-F5D2-4B8E-8F92-44228FE30100}`, version 10.0, `WrapperTool=tlbimp`).
> MSBuild generates `Interop.SAPbouiCOM.dll` / `Interop.SAPbobsCOM.dll` at build time —
> there are no standalone interop DLLs to reference. These GUIDs and the dev connection
> string match SAP's own SDK sample `Samples\COM UI DI\CSharp\New UI DI Connection`.

## Contents
1. Project setup (type, framework, bitness, references)
2. Connection flow (UI API, dev connection string, DI API)
3. Application loop & events
4. Menu entry
5. The Hello form (XML `.srf` and programmatic)
6. Add-on registration (`.ard`, deployment)
7. Common pitfalls
8. Minimal file list

---

## 1. Project Setup

**Project type — WinForms-style with a manual message loop, not a plain console.**
The add-on is a standalone `.exe` the SAP client launches as a separate process. The UI
API (`SAPbouiCOM`) is DCOM-based and pushes UI events back via COM callbacks. To receive
them the process must (a) run on an **STA thread** and (b) **pump a Windows message loop**.
Satisfy both with `[STAThread]` on `Main` plus `System.Windows.Forms.Application.Run()` as
the keep-alive — even though you never show a WinForm of your own (all UI is inside the SAP
client window). A `Console.ReadLine()` keep-alive looks like it works but starves the
message pump, so events silently never fire.

**Target framework.** Use **.NET Framework** (not .NET / Core / 5+). For 10.0, **4.8** is
the pragmatic default (4.6.1 is a safe floor). For 9.3, the 4.0/4.5 era; 4.6.1+ usually
still works.

**Platform target — must match the client bitness exactly.**
- SAP B1 **10.0 → 64-bit client → build `x64`.**
- SAP B1 **9.3 → 32-bit client → build `x86`.**
Do **not** use `AnyCPU`: on a 64-bit OS it resolves to 64-bit at runtime, which fails
against a 32-bit client with `BadImageFormatException` / "targets a different processor".
([SAP Community](https://community.sap.com/t5/enterprise-resource-planning-q-a/referenced-assembly-sapbouicom-dll-targets-a-different-processor/qaq-p/10969315))

**Referencing the interops.** Three options:
1. **COM reference** (`<COMReference … WrapperTool="tlbimp">`) — VS/MSBuild generates the
   interop from the **registered** type library at build time. This is what SAP's own
   samples use and what the scaffold uses. No DLLs to ship in source control.
2. Direct reference to a prebuilt `Interop.*.dll` (Browse → the SDK's matching-bitness
   folder). Use when you must pin an exact interop version for deployment.
3. NuGet — **no official package**; community ones are unofficial/version-locked. Avoid.

**`Embed Interop Types` → `False`** for both. Add-ons rely on COM **event interfaces**
(`_IApplicationEvents…`); type-embedding ("no-PIA") strips the event plumbing and causes
`InvalidCastException` on `item.Specific`. Ship `Interop.SAPbouiCOM.dll` +
`Interop.SAPbobsCOM.dll` next to the `.exe` (`Copy Local = True`).

---

## 2. Connection Flow

**Minimal connect (UI API):**

```csharp
var gui = new SAPbouiCOM.SboGuiApi();
gui.Connect(connectionString);            // attach to the running client
SAPbouiCOM.Application app = gui.GetApplication(-1);
```

**Where the connection string comes from:**
- **Registered/installed add-on (production):** SAP's Add-On Administration starts your
  `.exe` and passes the connection string as **`args[0]`** (an encrypted token). Read it
  from the command line; never hardcode in production.
- **Debugging from Visual Studio:** put the dev string in **Project Properties → Debug →
  Application arguments** (the scaffold pre-fills this in `<Name>.csproj.user`).

**Well-known development connection string** (attach to a client running on the same
machine with default settings — identical to SAP's SDK sample comment):
```
0030002C0030002C00530041005000420044005F00440061007400650076002C0050004C006F006D0056004900490056
```
Works when the client is already running and logged in on the same machine with default
UI-API security. If it's rejected, register the add-on once so SAP hands you the real
string, or align `SboGuiApi.SetSecurityLevel(...)` with the client's configured level.
([SAP blog: Connecting an add-on](https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-members/connecting-an-add-on-to-sap-business-one/ba-p/13254751))

**DI API `Company` from the UI session (no second login):**

```csharp
SAPbobsCOM.Company oCompany =
    (SAPbobsCOM.Company)SBO_Application.Company.GetDICompany();
```

`GetDICompany()` returns a DI `Company` already connected to the same company the user is
logged into — the simplest and SAP-recommended path (used by the "New UI DI Connection"
sample). The longer cookie route is an alternative when you need a fresh DI session:

```csharp
var company = new SAPbobsCOM.Company();
string cookie  = company.GetContextCookie();
string context = SBO_Application.Company.GetConnectionContext(cookie);
company.SetSboLoginContext(context);
int rc = company.Connect();   // 0 = OK; else company.GetLastError(out code, out msg)
```

The DI API install bitness must match the client (64-bit DI API for 10.0); a mismatched
`Interop.SAPbobsCOM` version is the usual "Could not load file or assembly Interop.SAPbobsCOM".

---

## 3. Application Loop & Events

Keep alive and pump events with `Application.Run()`; exit via `Application.Exit()` from the
shutdown handler.

**Install event filters first** so the client only marshals events you care about, on the
forms you care about — without filters every keystroke on every form is marshaled
cross-process (sluggishness/crashes):

```csharp
var filters = new SAPbouiCOM.EventFilters();
filters.Add(SAPbouiCOM.BoEventTypes.et_MENU_CLICK);              // not form-bound
var f = filters.Add(SAPbouiCOM.BoEventTypes.et_FORM_LOAD);    f.AddEx("MY_FORM_TYPE");
f     = filters.Add(SAPbouiCOM.BoEventTypes.et_ITEM_PRESSED); f.AddEx("MY_FORM_TYPE");
SBO_Application.SetFilter(filters);
```
([SAP Help — EventFilters](https://help.sap.com/doc/089315d8d0f8475a9fc84fb919b501a3/10.0/en-US/SDKHelp/SAPbouiCOM~EventFilters.html))

**Wire handlers** (note every UI handler has `out bool BubbleEvent` — set `false` to cancel
the event; guard on `pVal.BeforeAction` so logic doesn't run twice):

```csharp
SBO_Application.AppEvent  += OnAppEvent;    // void (BoAppEventTypes)
SBO_Application.MenuEvent += OnMenuEvent;   // (ref MenuEvent, out bool)
SBO_Application.ItemEvent += OnItemEvent;   // (string FormUID, ref ItemEvent, out bool)
```

`BoAppEventTypes`: `aet_ShutDown` (client closing), `aet_CompanyChanged`,
`aet_LanguageChanged`, `aet_ServerTerminition`. On shutdown/company-change: remove your
menu, release COM objects (`Marshal.FinalReleaseComObject`), then `Application.Exit()`.
Keep `SBO_Application` (and `SboGuiApi`) in **static/field** scope so the GC never collects
the event sink. ([SAP blog: Application Event in Detail](https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-members/application-event-in-detail/ba-p/12859930))

---

## 4. Menu Entry

Add an item under **Modules** (UID `43520`) and open the form on click:

```csharp
SAPbouiCOM.Menus modules = SBO_Application.Menus.Item("43520").SubMenus;
var p = (SAPbouiCOM.MenuCreationParams)SBO_Application.CreateObject(
            SAPbouiCOM.BoCreatableObjectType.cot_MenuCreationParams);
p.Type = SAPbouiCOM.BoMenuType.mt_STRING;
p.UniqueID = "MY_MENU";
p.String = "Say Hello";
p.Enabled = true;
modules.AddEx(p);
// then in MenuEvent: if (!pVal.BeforeAction && pVal.MenuUID == "MY_MENU") ShowForm();
```

`Menus` has `Exists(uid)` and `RemoveEx(uid)`. **Note:** the `Forms` collection has **no**
`Exists()` — probe with `Forms.Item(uid)` in a try/catch (it throws when not open).

---

## 5. The Hello Form

### (a) XML form definition (`.srf`) + `LoadBatchActions`

Root is `<Application><forms><action type="add"><form …>`. A **StaticText** label is
`item type="31"` (`it_STATIC`); its text is the `<specific caption="…"/>` attribute.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Application><forms><action type="add">
  <form appformnumber="MY_FORM" FormType="MY_FORM" type="0" uid="MY_FORM"
        title="Hello" left="400" top="100" width="300" height="160"
        client_width="292" client_height="132" BorderStyle="3" mode="3" visible="1">
    <datasources><userdatasources/><dbdatasources/></datasources>
    <items>
      <item uid="lblHello" type="31" left="20" top="20" width="250" height="20" visible="1">
        <AutoManagedAttribute/>
        <specific caption="Hello"/>
      </item>
    </items>
  </form>
</action></forms></Application>
```

Load it at runtime:

```csharp
var doc = new System.Xml.XmlDocument();
doc.Load(srfPath);                          // ship the .srf next to the .exe
string xml = doc.InnerXml;
SBO_Application.LoadBatchActions(ref xml);
string report = SBO_Application.GetLastBatchResults();  // XML report; check on failure
```
([SAP blog: The Secrets of SRF](https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-members/the-secrets-of-srf/ba-p/13271102))

### (b) Programmatic (`Forms.AddEx`)

```csharp
var par = (SAPbouiCOM.FormCreationParams)SBO_Application.CreateObject(
              SAPbouiCOM.BoCreatableObjectType.cot_FormCreationParams);
par.UniqueID = "MY_FORM"; par.FormType = "MY_FORM";
par.BorderStyle = SAPbouiCOM.BoFormBorderStyle.fbs_Fixed;
SAPbouiCOM.Form form = SBO_Application.Forms.AddEx(par);
form.Title = "Hello"; form.Width = 300; form.Height = 160;
SAPbouiCOM.Item item = form.Items.Add("lblHello", SAPbouiCOM.BoFormItemTypes.it_STATIC);
item.Left = 20; item.Top = 20; item.Width = 250; item.Height = 20;
((SAPbouiCOM.StaticText)item.Specific).Caption = "Hello";
form.Visible = true;
```

XML/SRF is the SAP-recommended route for non-trivial forms (Screen Painter edits it,
AutoManagedAttributes give mode-based show/hide). Programmatic is fine for tiny forms.

---

## 6. Add-On Registration (deployment only)

Only needed to **install** the add-on so SAP auto-starts it; not needed to debug from VS.

1. Generate an **`.ard`** with the **Add-On Reg Data Generator**:
   `…\SAP Business One SDK\Tools\AddOnRegDataGen\AddOnRegDataGen.exe`. Fill Partner/Add-On
   info, point **Install** at your built `.exe`, Generate.
2. In SAP: **Administration → Add-Ons → Add-On Administration → Register Add-On**, browse
   to the `.ard`, assign it, choose Automatic/Manual startup. On next login SAP launches
   your `.exe` and passes the real connection string as `args[0]`.

---

## 7. Common Pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Bitness mismatch | `BadImageFormatException`, "targets a different processor" | x64 for 10.0, x86 for 9.3. Never AnyCPU. |
| Interop version mismatch | "Could not load … Interop.SAPbobsCOM, Version=…" | Ship the interops you built against (`Copy Local`); match the target PL. |
| `Embed Interop Types = True` | events never fire; `InvalidCastException` on `.Specific` | Set it **False** for both COM refs. |
| No STA / no message loop | connects then exits; zero events | `[STAThread]` + `Application.Run()`. Don't use `Console.ReadLine()`. |
| Missing event filter | client sluggish; intermittent crashes | `SetFilter(EventFilters)` for only the events/forms you need. |
| Handling event twice | logic runs on before+after | guard `if (pVal.BeforeAction) …`. |
| Connect fails | "UI API Server – Server is Down", "Could not find SBO…" | client must be running+logged in first; match elevation (run VS as admin if client is); correct dev string / `SetSecurityLevel`. |
| GC collects sink | events stop after a while | keep `SBO_Application` / `SboGuiApi` in static fields. |
| Client won't close | hung client on exit | handle `aet_ShutDown` → release COM (`Marshal.FinalReleaseComObject`) → `Application.Exit()`. |
| `Forms.Exists` not found (compile) | CS1061 | `Forms` has no `Exists`; probe `Forms.Item(uid)` in try/catch. |

---

## 8. Minimal File List

| File | Purpose |
|---|---|
| `<Name>.csproj` | .NET FW project; `v4.8`; `PlatformTarget x64`; COMReference to SAPbouiCOM/SAPbobsCOM (`EmbedInteropTypes=False`); refs `System.Windows.Forms`, `System.Xml`. |
| `<Name>.csproj.user` | Dev connection string in `<StartArguments>` so F5 attaches. |
| `Program.cs` | `[STAThread] Main` → `new Addon(args)` → `Application.Run()`. |
| `Addon.cs` | Connect UI (+DI via `GetDICompany`), event filters, `AppEvent`/`MenuEvent`, menu item, Hello form (XML + code). |
| `HelloForm.srf` | XML form: one `it_STATIC` (`type="31"`) with `caption="Hello"`. `Copy if newer`. |
| `Properties/AssemblyInfo.cs`, `App.config` | Standard metadata / runtime version. |
| `<Name>.ard` | *(deployment only)* from `AddOnRegDataGen.exe`. |

**Bare minimum to show "Hello":** `<Name>.csproj` + `Program.cs` + `Addon.cs` + (`HelloForm.srf`
or the programmatic form method).

### Key sources
- [Connecting an add-on to SAP Business One](https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-members/connecting-an-add-on-to-sap-business-one/ba-p/13254751)
- [SAP Help SDK — SAPbouiCOM](https://help.sap.com/doc/089315d8d0f8475a9fc84fb919b501a3/10.0/en-US/SDKHelp/SAPbouiCOM_P.html) · [EventFilters](https://help.sap.com/doc/089315d8d0f8475a9fc84fb919b501a3/10.0/en-US/SDKHelp/SAPbouiCOM~EventFilters.html) · [BoFormItemTypes](https://help.sap.com/doc/089315d8d0f8475a9fc84fb919b501a3/10.0/en-US/SDKHelp/SAPbouiCOM~Enumerations~BoFormItemTypes_EN.html)
- [The Secrets of SRF](https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-members/the-secrets-of-srf/ba-p/13271102) · [Application Event in Detail](https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-members/application-event-in-detail/ba-p/12859930)
- Local ground truth: `…\SAP Business One SDK\Samples\COM UI DI\CSharp\New UI DI Connection`
