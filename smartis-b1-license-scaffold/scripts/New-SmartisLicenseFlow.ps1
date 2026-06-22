<#
.SYNOPSIS
    Scaffold the Smartis SAP B1 license layer into a target add-on project by copying the
    canonical code bundled in this skill (assets/) and rewriting the root namespace.

.DESCRIPTION
    Copies:
      assets/Helpers/*.cs   -> <TargetProjectDir>/Helpers/
      assets/Models/*.cs    -> <TargetProjectDir>/Models/
      assets/DataScripts/** -> <TargetProjectDir>/DataScripts/   (merged, structure preserved)
    In the copied .cs files it replaces ONLY the root namespace token:
      VASManager.Helpers -> <RootNamespace>.Helpers
      VASManager.Models  -> <RootNamespace>.Models

    It does NOT build, run, or touch the database. It cannot guess your base class, service layer,
    proc name or connection string — finish those by hand (see the printed checklist / SKILL.md).

.PARAMETER TargetProjectDir
    The add-on project folder that contains the .csproj (e.g. ...\YourAddon\YourAddon).

.PARAMETER RootNamespace
    The target project's root namespace (e.g. "YourAddon" or "Smartis.EInvoice.B1Addon").

.PARAMETER Force
    Overwrite existing files in the target.

.PARAMETER DryRun
    Print what would happen without copying anything.

.EXAMPLE
    .\New-SmartisLicenseFlow.ps1 -TargetProjectDir "D:\...\Consolidation\Consolidation" -RootNamespace "Consolidation"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $TargetProjectDir,
    [Parameter(Mandatory = $true)] [string] $RootNamespace,
    [switch] $Force,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

$skillRoot = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $skillRoot 'assets'

if (-not (Test-Path -LiteralPath $assetsDir)) {
    throw "assets folder not found at '$assetsDir'. Run this script from inside the skill."
}
if (-not (Test-Path -LiteralPath $TargetProjectDir)) {
    throw "Target project dir not found: '$TargetProjectDir'"
}

Write-Host "Skill assets : $assetsDir"
Write-Host "Target       : $TargetProjectDir"
Write-Host "Namespace    : $RootNamespace"
if ($DryRun) { Write-Host "MODE         : DRY RUN (no files written)" -ForegroundColor Yellow }
Write-Host ""

function Copy-CsWithNamespace {
    param([string] $SrcFile, [string] $DestFile)

    $content = Get-Content -LiteralPath $SrcFile -Raw -Encoding UTF8
    $content = $content.Replace('VASManager.Helpers', ($RootNamespace + '.Helpers'))
    $content = $content.Replace('VASManager.Models',  ($RootNamespace + '.Models'))

    $destDir = Split-Path -Parent $DestFile
    if ($DryRun) {
        Write-Host "  [cs ] $DestFile"
        return
    }
    if ((Test-Path -LiteralPath $DestFile) -and -not $Force) {
        Write-Host "  [skip] exists (use -Force): $DestFile" -ForegroundColor DarkYellow
        return
    }
    if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    # UTF-8 with BOM keeps Visual Studio happy and preserves the non-ASCII chars in the source.
    Set-Content -LiteralPath $DestFile -Value $content -Encoding UTF8
    Write-Host "  [cs ] $DestFile"
}

function Copy-Plain {
    param([string] $SrcFile, [string] $DestFile)

    $destDir = Split-Path -Parent $DestFile
    if ($DryRun) {
        Write-Host "  [sql] $DestFile"
        return
    }
    if ((Test-Path -LiteralPath $DestFile) -and -not $Force) {
        Write-Host "  [skip] exists (use -Force): $DestFile" -ForegroundColor DarkYellow
        return
    }
    if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    Copy-Item -LiteralPath $SrcFile -Destination $DestFile -Force
    Write-Host "  [sql] $DestFile"
}

# --- C# (namespace-rewritten) ---
Write-Host "C# files:"
foreach ($sub in @('Helpers', 'Models')) {
    $srcSub = Join-Path $assetsDir $sub
    if (-not (Test-Path -LiteralPath $srcSub)) { continue }
    Get-ChildItem -LiteralPath $srcSub -Filter *.cs -File | ForEach-Object {
        Copy-CsWithNamespace -SrcFile $_.FullName -DestFile (Join-Path (Join-Path $TargetProjectDir $sub) $_.Name)
    }
}

# --- DataScripts (verbatim, structure preserved) ---
Write-Host ""
Write-Host "SQL files:"
$srcScripts = Join-Path $assetsDir 'DataScripts'
if (Test-Path -LiteralPath $srcScripts) {
    Get-ChildItem -LiteralPath $srcScripts -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($srcScripts.Length).TrimStart('\', '/')
        $dest = Join-Path (Join-Path $TargetProjectDir 'DataScripts') $rel
        Copy-Plain -SrcFile $_.FullName -DestFile $dest
    }
}

Write-Host ""
Write-Host "Done copying. Manual steps still required (the script cannot infer these):" -ForegroundColor Cyan
Write-Host "  1. Base class: replace 'using UIAPI;' + SapUiBase.* with your DI/UI base class."
Write-Host "  2. Service layer: replace 'using SDXManager.Service_Layer;' + 'new SDXServiceLayer()'."
Write-Host "  3. ValidateAddonIdentifier connection string: kept hardcoded (fixed constant) - no change needed; the per-server value is the license IdentifierKey, read from the license."
Write-Host "  4. Load AddOnVersionModel from YOUR .ard at startup (see references/integration-guide.md)."
Write-Host "  5. GetAddonInfo proc name 'VASGetAddonInfo' -> keep or rename in all 3 places."
Write-Host "  6. Ensure 00.INIT scripts run before any license read; deploy GetAddonInfo proc."
Write-Host "  7. Add the new .cs + .sql files to the .csproj (and set .sql CopyToOutputDirectory/embedded as your project does)."
Write-Host "  8. If S0SADC/S0ADDONDATA_SP already exist on the shared DB, reconcile with skill 'smartis-s0sadc-standardize' (identical body across add-ons)."
Write-Host ""
Write-Host "Do NOT build/run for the user — they build & deploy and report back (Smartis project rule)." -ForegroundColor Cyan
