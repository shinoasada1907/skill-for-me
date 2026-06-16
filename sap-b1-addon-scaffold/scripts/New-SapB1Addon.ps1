#requires -version 5
<#
.SYNOPSIS
    Scaffold a minimal SAP Business One add-on (UI API + DI API) that connects to a
    running SAP B1 client and shows a "Hello" form. Targets SAP B1 10.0 (x64) by default.

.DESCRIPTION
    Generates a complete, buildable .NET Framework project:
      <Name>/
        <Name>.sln
        <Name>.csproj            classic MSBuild project, COMReference to SAPbouiCOM/SAPbobsCOM
        <Name>.csproj.user       dev connection string pre-filled in Debug > Application arguments
        Program.cs               [STAThread] Main -> new Addon(args); Application.Run()
        Addon.cs                 connect UI (+DI), event filters, menu item, Hello form
        HelloForm.srf            XML form definition (when -FormStyle xml)
        Properties/AssemblyInfo.cs
        App.config
        README.md

    With -Build it also compiles the project with MSBuild so you get immediate proof
    the COM references resolve and the code is valid on this machine.

.EXAMPLE
    .\New-SapB1Addon.ps1 -Name MyAddon -OutputDir D:\Code\Addons

.EXAMPLE
    .\New-SapB1Addon.ps1 -Name DemoAddon -OutputDir D:\Tmp -Connect UI -FormStyle code -Build
#>
[CmdletBinding()]
param(
    # Add-on name. Used for the assembly, the project file and (by default) the namespace.
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')]
    [string]$Name,

    # Parent folder. The project is created in <OutputDir>\<Name>.
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    # Root namespace (defaults to -Name).
    [string]$Namespace,

    # SAP B1 version -> drives interop type-library version and default bitness.
    [ValidateSet('10.0', '9.3')]
    [string]$Version = '10.0',

    # Platform target. Defaults: 10.0 -> x64, 9.3 -> x86. Never AnyCPU (bitness must match the client).
    [ValidateSet('x64', 'x86')]
    [string]$Platform,

    # UIDI = connect UI API and also grab a DI API Company. UI = UI API only (lighter).
    [ValidateSet('UIDI', 'UI')]
    [string]$Connect = 'UIDI',

    # xml  = Hello form from a bundled HelloForm.srf (Screen-Painter friendly).
    # code = Hello form built entirely in C# (no .srf file).
    [ValidateSet('xml', 'code')]
    [string]$FormStyle = 'xml',

    # Compile with MSBuild after generating.
    [switch]$Build,

    # Overwrite an existing target folder.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Derive values
# ---------------------------------------------------------------------------
if (-not $Namespace) { $Namespace = $Name }
if (-not $Platform) { $Platform = if ($Version -eq '10.0') { 'x64' } else { 'x86' } }

# COM type-library version per SAP B1 release. The type-library GUIDs are stable
# across releases; only the major/minor version changes.
$comMajor = if ($Version -eq '10.0') { '10' } else { '9' }
$comMinor = if ($Version -eq '10.0') { '0' } else { '30' }
$tfmVersion = '4.8'   # .NET Framework target (v4.8 for SAP B1 10.0)

# Well-known SAP development connection string: attaches to a SAP B1 client running
# on the SAME machine with default settings. (Same value SAP ships in its own SDK
# "New UI DI Connection" sample.) Production add-ons receive the real string from SAP.
$devConn = '0030002C0030002C00530041005000420044005F00440061007400650076002C0050004C006F006D0056004900490056'

$formType = "${Name}_Hello"
$menuUid  = "${Name}_HelloMenu"
$projGuid = '{' + ([guid]::NewGuid().ToString().ToUpper()) + '}'
$asmGuid  = ([guid]::NewGuid().ToString())
$useXml   = if ($FormStyle -eq 'xml') { 'true' } else { 'false' }

# DI-specific snippets (empty in UI-only mode).
if ($Connect -eq 'UIDI') {
    $diField   = '        // DI API company, sharing the user''s existing UI session.' + "`r`n" + '        private SAPbobsCOM.Company oCompany;'
    $diConnect = '            // Reuse the UI''s DI connection (no extra login needed).' + "`r`n" + '            oCompany = (SAPbobsCOM.Company)SBO_Application.Company.GetDICompany();'
    $diComRef  = @'
    <COMReference Include="SAPbobsCOM">
      <Guid>{FC8030BE-F5D2-4B8E-8F92-44228FE30100}</Guid>
      <VersionMajor>{{COM_MAJOR}}</VersionMajor>
      <VersionMinor>{{COM_MINOR}}</VersionMinor>
      <Lcid>0</Lcid>
      <WrapperTool>tlbimp</WrapperTool>
      <EmbedInteropTypes>False</EmbedInteropTypes>
    </COMReference>
'@
}
else {
    $diField   = ''
    $diConnect = ''
    $diComRef  = ''
}
# The DI COMReference is injected into the csproj AFTER the COM_MAJOR/MINOR tokens are
# expanded, so substitute its versions up-front here.
$diComRef = $diComRef.Replace('{{COM_MAJOR}}', $comMajor).Replace('{{COM_MINOR}}', $comMinor)

# srf <Content> item only when we actually emit a .srf file.
if ($FormStyle -eq 'xml') {
    $srfContent = @'
  <ItemGroup>
    <Content Include="HelloForm.srf">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
'@
}
else {
    $srfContent = ''
}

# ---------------------------------------------------------------------------
# Templates (single-quoted here-strings = no PowerShell interpolation)
# ---------------------------------------------------------------------------
$tplProgram = @'
using System;
using System.Windows.Forms;

namespace {{NAMESPACE}}
{
    /// <summary>
    /// Entry point. SAP B1 launches this exe as a separate process and passes the
    /// connection string as the first argument. We must run on an STA thread and pump
    /// a Windows message loop (Application.Run) so the UI API can deliver COM events.
    /// </summary>
    internal static class Program
    {
        // Rooted in a static field so the add-on (and its COM event sinks) are never
        // garbage-collected for the lifetime of the process.
        private static Addon _addon;

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                _addon = new Addon(args);
                Application.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "{{ADDON_NAME}} failed to start",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
'@

$tplAddon = @'
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;

namespace {{NAMESPACE}}
{
    /// <summary>
    /// Core add-on object. Owns the UI API connection (and optionally a DI API company),
    /// installs event filters, adds a menu entry under "Modules" and shows a Hello form.
    /// </summary>
    internal sealed class Addon
    {
        // Unique ids. Prefixing with the add-on name avoids clashing with other add-ons.
        private const string HelloFormType = "{{FORM_TYPE}}";
        private const string MenuUid       = "{{MENU_UID}}";

        // true  -> build the Hello form from the bundled HelloForm.srf (XML).
        // false -> build the same form in code (no .srf needed).
        private static readonly bool UseXmlForm = {{USE_XML_FORM}};

        // Rooted in fields so the GC never collects the COM objects / event sink.
        private SAPbouiCOM.SboGuiApi _gui;
        private SAPbouiCOM.Application SBO_Application;
{{DI_FIELD}}

        public Addon(string[] args)
        {
            ConnectToClient(args);   // UI API
{{DI_CONNECT}}
            SetEventFilters();        // only receive the events we care about (perf!)
            WireEvents();
            AddMenuItem();
            ShowHelloForm();          // open immediately so F5 shows something

            SBO_Application.StatusBar.SetText(
                "{{ADDON_NAME}} loaded.",
                SAPbouiCOM.BoMessageTime.bmt_Short,
                SAPbouiCOM.BoStatusBarMessageType.smt_Success);
        }

        private void ConnectToClient(string[] args)
        {
            _gui = new SAPbouiCOM.SboGuiApi();

            // args[0] = connection string. SAP supplies it for a registered add-on; when
            // debugging from Visual Studio it comes from Project Properties > Debug >
            // Application arguments (already filled in {{ADDON_NAME}}.csproj.user).
            if (args == null || args.Length < 1 || string.IsNullOrEmpty(args[0]))
            {
                throw new Exception(
                    "No connection string supplied.\r\n\r\n" +
                    "When running from Visual Studio: open the SAP Business One client and log " +
                    "into a company first, and make sure the development connection string is set " +
                    "in Project Properties > Debug > Application arguments.");
            }

            _gui.Connect(args[0]);
            SBO_Application = _gui.GetApplication(-1);
        }

        private void SetEventFilters()
        {
            var filters = new SAPbouiCOM.EventFilters();

            // Menu clicks are not form-bound -> add without AddEx.
            filters.Add(SAPbouiCOM.BoEventTypes.et_MENU_CLICK);

            // Only our own form's events.
            var f = filters.Add(SAPbouiCOM.BoEventTypes.et_FORM_LOAD);    f.AddEx(HelloFormType);
            f     = filters.Add(SAPbouiCOM.BoEventTypes.et_ITEM_PRESSED); f.AddEx(HelloFormType);

            SBO_Application.SetFilter(filters);
        }

        private void WireEvents()
        {
            SBO_Application.AppEvent  += OnAppEvent;
            SBO_Application.MenuEvent += OnMenuEvent;
        }

        // Application lifecycle: shut down cleanly when the client closes or the company changes.
        private void OnAppEvent(SAPbouiCOM.BoAppEventTypes eventType)
        {
            switch (eventType)
            {
                case SAPbouiCOM.BoAppEventTypes.aet_ShutDown:
                case SAPbouiCOM.BoAppEventTypes.aet_ServerTerminition:
                case SAPbouiCOM.BoAppEventTypes.aet_CompanyChanged:
                    try { SBO_Application.Menus.RemoveEx(MenuUid); } catch { /* may not exist */ }
                    Application.Exit();   // unblocks Application.Run() -> process ends
                    break;
            }
        }

        private void OnMenuEvent(ref SAPbouiCOM.MenuEvent pVal, out bool bubbleEvent)
        {
            bubbleEvent = true;
            if (pVal.BeforeAction) return;          // act once, on the "after" pass
            if (pVal.MenuUID == MenuUid) ShowHelloForm();
        }

        private void AddMenuItem()
        {
            if (SBO_Application.Menus.Exists(MenuUid)) return;

            // "43520" is the standard "Modules" menu.
            SAPbouiCOM.Menus modules = SBO_Application.Menus.Item("43520").SubMenus;

            var p = (SAPbouiCOM.MenuCreationParams)SBO_Application.CreateObject(
                        SAPbouiCOM.BoCreatableObjectType.cot_MenuCreationParams);
            p.Type     = SAPbouiCOM.BoMenuType.mt_STRING;
            p.UniqueID = MenuUid;
            p.String   = "{{ADDON_NAME}} - Hello";
            p.Enabled  = true;
            p.Position = -1;

            modules.AddEx(p);
        }

        private void ShowHelloForm()
        {
            // Forms has no Exists(); a form that is already open is simply re-selected.
            if (FormExists(HelloFormType))
            {
                SBO_Application.Forms.Item(HelloFormType).Select();
                return;
            }

            if (UseXmlForm) ShowHelloFormXml();
            else            ShowHelloFormCode();
        }

        // Forms.Item() throws when the form is not open, so probe with try/catch.
        private bool FormExists(string uid)
        {
            try { return SBO_Application.Forms.Item(uid) != null; }
            catch { return false; }
        }

        // (a) Load the form from the bundled HelloForm.srf via LoadBatchActions.
        private void ShowHelloFormXml()
        {
            string srf = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "HelloForm.srf");

            var doc = new XmlDocument();
            doc.Load(srf);
            string xml = doc.InnerXml;

            SBO_Application.LoadBatchActions(ref xml);

            // GetLastBatchResults() returns an XML report; inspect it if the form never appears.
            string report = SBO_Application.GetLastBatchResults();
            if (!FormExists(HelloFormType))
                SBO_Application.MessageBox("Could not load HelloForm.srf:\r\n" + report);
        }

        // (b) Build the form entirely in code (no .srf file).
        private void ShowHelloFormCode()
        {
            var p = (SAPbouiCOM.FormCreationParams)SBO_Application.CreateObject(
                        SAPbouiCOM.BoCreatableObjectType.cot_FormCreationParams);
            p.UniqueID    = HelloFormType;
            p.FormType    = HelloFormType;
            p.BorderStyle = SAPbouiCOM.BoFormBorderStyle.fbs_Fixed;

            SAPbouiCOM.Form form = SBO_Application.Forms.AddEx(p);
            form.Title  = "{{ADDON_NAME}}";
            form.Left   = 400;  form.Top    = 100;
            form.Width  = 300;  form.Height = 160;

            SAPbouiCOM.Item item = form.Items.Add("lblHello", SAPbouiCOM.BoFormItemTypes.it_STATIC);
            item.Left = 20; item.Top = 20; item.Width = 250; item.Height = 20;
            ((SAPbouiCOM.StaticText)item.Specific).Caption = "Hello, {{ADDON_NAME}}!";

            form.Visible = true;
        }
    }
}
'@

$tplCsproj = @'
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="14.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003" DefaultTargets="Build">
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">{{PLATFORM}}</Platform>
    <ProjectGuid>{{PROJECT_GUID}}</ProjectGuid>
    <OutputType>WinExe</OutputType>
    <RootNamespace>{{NAMESPACE}}</RootNamespace>
    <AssemblyName>{{ADDON_NAME}}</AssemblyName>
    <TargetFrameworkVersion>{{TFM}}</TargetFrameworkVersion>
    <StartupObject>{{NAMESPACE}}.Program</StartupObject>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|{{PLATFORM}}' ">
    <PlatformTarget>{{PLATFORM}}</PlatformTarget>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>bin\Debug\</OutputPath>
    <DefineConstants>DEBUG;TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|{{PLATFORM}}' ">
    <PlatformTarget>{{PLATFORM}}</PlatformTarget>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>bin\Release\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>
  <ItemGroup>
    <COMReference Include="SAPbouiCOM">
      <Guid>{6048236A-956D-498D-A6F1-9C81C13AB6E8}</Guid>
      <VersionMajor>{{COM_MAJOR}}</VersionMajor>
      <VersionMinor>{{COM_MINOR}}</VersionMinor>
      <Lcid>0</Lcid>
      <WrapperTool>tlbimp</WrapperTool>
      <EmbedInteropTypes>False</EmbedInteropTypes>
    </COMReference>
{{DI_COMREF}}
    <Reference Include="System" />
    <Reference Include="System.Data" />
    <Reference Include="System.Drawing" />
    <Reference Include="System.Windows.Forms" />
    <Reference Include="System.Xml" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="Addon.cs" />
    <Compile Include="Properties\AssemblyInfo.cs" />
  </ItemGroup>
{{SRF_CONTENT}}
  <ItemGroup>
    <None Include="App.config" />
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
'@

$tplCsprojUser = @'
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="14.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|{{PLATFORM}}' ">
    <StartAction>Project</StartAction>
    <StartArguments>{{CONN_STRING}}</StartArguments>
  </PropertyGroup>
</Project>
'@

$tplAssemblyInfo = @'
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("{{ADDON_NAME}}")]
[assembly: AssemblyDescription("SAP Business One add-on")]
[assembly: AssemblyProduct("{{ADDON_NAME}}")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]
[assembly: Guid("{{ASM_GUID}}")]
'@

$tplAppConfig = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup>
    <supportedRuntime version="v4.0" sku=".NETFramework,Version={{TFM}}" />
  </startup>
</configuration>
'@

$tplSrf = @'
<?xml version="1.0" encoding="UTF-8"?>
<Application>
  <forms>
    <action type="add">
      <form appformnumber="{{FORM_TYPE}}" FormType="{{FORM_TYPE}}" type="0" uid="{{FORM_TYPE}}" title="{{ADDON_NAME}}" left="400" top="100" width="300" height="160" client_width="292" client_height="132" BorderStyle="3" color="0" mode="3" default_button="" pane="0" visible="1">
        <datasources>
          <userdatasources />
          <dbdatasources />
        </datasources>
        <items>
          <item uid="lblHello" type="31" left="20" top="20" width="250" height="20" visible="1" enabled="1" from_pane="0" to_pane="0">
            <AutoManagedAttribute />
            <specific caption="Hello, {{ADDON_NAME}}!" />
          </item>
        </items>
      </form>
    </action>
  </forms>
</Application>
'@

$tplSln = @'
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "{{ADDON_NAME}}", "{{ADDON_NAME}}.csproj", "{{PROJECT_GUID}}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|{{PLATFORM}} = Debug|{{PLATFORM}}
		Release|{{PLATFORM}} = Release|{{PLATFORM}}
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{{PROJECT_GUID}}.Debug|{{PLATFORM}}.ActiveCfg = Debug|{{PLATFORM}}
		{{PROJECT_GUID}}.Debug|{{PLATFORM}}.Build.0 = Debug|{{PLATFORM}}
		{{PROJECT_GUID}}.Release|{{PLATFORM}}.ActiveCfg = Release|{{PLATFORM}}
		{{PROJECT_GUID}}.Release|{{PLATFORM}}.Build.0 = Release|{{PLATFORM}}
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
'@

$tplReadme = @'
# {{ADDON_NAME}} - SAP Business One Add-On

Minimal **UI API + DI API** add-on for SAP B1 {{VERSION}} ({{PLATFORM}}).
On start it connects to the running SAP client, adds a menu item under **Modules**,
and shows a "Hello" form.

## Run / Debug (F5)

1. Open **{{ADDON_NAME}}.sln** in Visual Studio.
2. Make sure the **SAP Business One client is running and logged into a company** on this machine.
3. Press **F5**.

The development connection string is already filled in `{{ADDON_NAME}}.csproj.user`
(Project Properties > Debug > Application arguments), so F5 attaches to the running
client, the menu item appears under *Modules*, and the Hello form opens.

## How it connects

- UI API: `SboGuiApi.Connect(args[0])` then `GetApplication()`. `args[0]` is the
  connection string - the dev string in debug; SAP supplies the real one for a
  registered add-on.
- DI API: `oCompany = (SAPbobsCOM.Company)SBO_Application.Company.GetDICompany();`
  (reuses the user's existing UI session - no second login).

## Bitness matters

This project targets **{{PLATFORM}}**, matching a SAP B1 {{VERSION}} client. Never build
`AnyCPU`: a 64-bit client cannot load a 32-bit add-on and vice versa.

## Deploy as a real add-on (optional)

1. Run `...\SAP Business One SDK\Tools\AddOnRegDataGen\AddOnRegDataGen.exe`, point
   "Install" at your built `.exe`, and generate a `.ard` file.
2. In SAP: **Administration > Add-Ons > Add-On Administration > Register Add-On**,
   browse to the `.ard`, assign it and choose a startup mode.

## Troubleshooting

- **"UI API Server - Server is Down" / connect fails**: the client must be running and
  logged in first; run Visual Studio at the same elevation as the client (admin/non-admin).
- **`BadImageFormatException` / "targets a different processor"**: bitness mismatch -
  keep the project on {{PLATFORM}}.
- **Events never fire / `InvalidCastException` on `.Specific`**: make sure the COM
  references have `Embed Interop Types = False` (they do in this project).
- **Hello form does not appear (XML mode)**: a message box shows the LoadBatchActions
  report; usually a malformed `.srf` or a duplicate form UID.
'@

# ---------------------------------------------------------------------------
# Token replacement
# ---------------------------------------------------------------------------
function Expand-Template {
    param([string]$Text)
    return $Text.
        Replace('{{ADDON_NAME}}',  $Name).
        Replace('{{NAMESPACE}}',   $Namespace).
        Replace('{{VERSION}}',     $Version).
        Replace('{{PLATFORM}}',    $Platform).
        Replace('{{TFM}}',         "v$([string]$tfmVersion)").
        Replace('{{COM_MAJOR}}',   $comMajor).
        Replace('{{COM_MINOR}}',   $comMinor).
        Replace('{{FORM_TYPE}}',   $formType).
        Replace('{{MENU_UID}}',    $menuUid).
        Replace('{{PROJECT_GUID}}',$projGuid).
        Replace('{{ASM_GUID}}',    $asmGuid).
        Replace('{{CONN_STRING}}', $devConn).
        Replace('{{USE_XML_FORM}}',$useXml).
        Replace('{{DI_FIELD}}',    $diField).
        Replace('{{DI_CONNECT}}',  $diConnect).
        Replace('{{DI_COMREF}}',   $diComRef).
        Replace('{{SRF_CONTENT}}', $srfContent)
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

# ---------------------------------------------------------------------------
# Create the project
# ---------------------------------------------------------------------------
$projDir = Join-Path $OutputDir $Name
if (Test-Path $projDir) {
    if (-not $Force) { throw "Target folder already exists: $projDir  (use -Force to overwrite)" }
}
else {
    New-Item -ItemType Directory -Path $projDir -Force | Out-Null
}
New-Item -ItemType Directory -Path (Join-Path $projDir 'Properties') -Force | Out-Null

# Order matters only for readability.
Write-Utf8NoBom (Join-Path $projDir "$Name.sln")              (Expand-Template $tplSln)
Write-Utf8NoBom (Join-Path $projDir "$Name.csproj")           (Expand-Template $tplCsproj)
Write-Utf8NoBom (Join-Path $projDir "$Name.csproj.user")      (Expand-Template $tplCsprojUser)
Write-Utf8NoBom (Join-Path $projDir 'Program.cs')             (Expand-Template $tplProgram)
Write-Utf8NoBom (Join-Path $projDir 'Addon.cs')               (Expand-Template $tplAddon)
Write-Utf8NoBom (Join-Path $projDir 'Properties\AssemblyInfo.cs') (Expand-Template $tplAssemblyInfo)
Write-Utf8NoBom (Join-Path $projDir 'App.config')             (Expand-Template $tplAppConfig)
Write-Utf8NoBom (Join-Path $projDir 'README.md')              (Expand-Template $tplReadme)
if ($FormStyle -eq 'xml') {
    Write-Utf8NoBom (Join-Path $projDir 'HelloForm.srf')      (Expand-Template $tplSrf)
}

Write-Host ""
Write-Host "Created SAP B1 add-on '$Name' at:" -ForegroundColor Green
Write-Host "  $projDir"
Write-Host "  version=$Version  platform=$Platform  connect=$Connect  form=$FormStyle"
Write-Host ""

# ---------------------------------------------------------------------------
# Optional build
# ---------------------------------------------------------------------------
function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $p = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($p -and (Test-Path $p)) { return $p }
    }
    foreach ($k in @(
        'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe')) {
        if (Test-Path $k) { return $k }
    }
    $cmd = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

if ($Build) {
    $msbuild = Find-MSBuild
    if (-not $msbuild) {
        Write-Warning "MSBuild not found - skipping build. Open $Name.sln in Visual Studio to build."
    }
    else {
        Write-Host "Building with: $msbuild" -ForegroundColor Cyan
        $csproj = Join-Path $projDir "$Name.csproj"
        & $msbuild $csproj "/p:Configuration=Debug" "/p:Platform=$Platform" "/v:minimal" "/nologo"
        if ($LASTEXITCODE -eq 0) {
            Write-Host "BUILD SUCCEEDED" -ForegroundColor Green
            Write-Host "Output: $(Join-Path $projDir 'bin\Debug')"
        }
        else {
            Write-Warning "BUILD FAILED (exit $LASTEXITCODE). See MSBuild output above."
        }
    }
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Open $Name.sln in Visual Studio."
Write-Host "  2. Start the SAP Business One client and log into a company."
Write-Host "  3. Press F5 - the add-on attaches and the Hello form appears."
