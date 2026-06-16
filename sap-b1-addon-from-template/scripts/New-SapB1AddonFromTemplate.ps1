#requires -version 5
<#
.SYNOPSIS
    Create a new SAP Business One add-on solution by cloning the user's .NET add-on
    template and renaming everything to a new add-on name.

.DESCRIPTION
    The template (bundled inside this skill at assets/template) is a .NET 10
    (net10.0-windows / WinForms) SAP B1 add-on with a 4-project layered architecture
    (Presentation / BLL / DAL / Common) and base classes for the UI API, DI API and
    Service Layer. The template uses a single identifier token "TemplateAddOnDotNetCore"
    for the solution, every project, folder, namespace and project reference.

    This script copies the template (excluding build/VCS artifacts), renames every file
    and folder that contains the token, and replaces the token inside every text file -
    producing a ready-to-build solution named after -Name.

.EXAMPLE
    .\New-SapB1AddonFromTemplate.ps1 -Name WorkOrderAddon -OutputDir D:\Code\Addons -Build

.EXAMPLE
    .\New-SapB1AddonFromTemplate.ps1 -Name InventoryTools -OutputDir D:\Code -TemplatePath D:\Code\Template\TemplateAddOnDotNetCore
#>
[CmdletBinding()]
param(
    # New add-on name. Becomes the solution, every project (<Name>, <Name>.BLL, <Name>.DAL,
    # <Name>.Common), folders and root namespaces. Must be a valid C# identifier
    # (letters/digits/underscore, optional dots; starts with a letter).
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)*$')]
    [string]$Name,

    # Parent folder. Defaults to the current directory. Solution is created in <OutputDir>\<Name>.
    [string]$OutputDir,

    # Path to the template to use. Defaults to the copy BUNDLED INSIDE this skill
    # (assets/template), so it works on any machine with no external template folder.
    [string]$TemplatePath,

    # The identifier token in the template that gets replaced by -Name.
    [string]$Token = 'TemplateAddOnDotNetCore',

    # Run "dotnet build" on the new solution after generating.
    [switch]$Build,

    # Overwrite an existing target folder.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Resolve template (default: the copy bundled inside this skill at assets/template)
# ---------------------------------------------------------------------------
if (-not $TemplatePath) {
    $TemplatePath = Join-Path $PSScriptRoot '..\assets\template'
}
if (-not (Test-Path $TemplatePath)) { throw "Template not found: $TemplatePath" }
$src = (Resolve-Path $TemplatePath).Path.TrimEnd('\')

if ($Name -eq $Token) { throw "-Name must differ from the template token '$Token'." }

if (-not $OutputDir) { $OutputDir = (Get-Location).Path }
$dst = Join-Path $OutputDir $Name
if (Test-Path $dst) {
    if (-not $Force) { throw "Target folder already exists: $dst  (use -Force to overwrite)" }
    Remove-Item $dst -Recurse -Force
}
New-Item -ItemType Directory -Path $dst -Force | Out-Null

# ---------------------------------------------------------------------------
# 1) Copy the template tree, excluding build / VCS artifacts
# ---------------------------------------------------------------------------
$excludeDirs = @('bin', 'obj', '.vs', '.git', '.idea', 'packages', 'TestResults')
$srcLen = $src.Length
$copied = 0
Get-ChildItem $src -Recurse -File -Force -ErrorAction SilentlyContinue | Where-Object {
    $rel = $_.FullName.Substring($srcLen).TrimStart('\')
    $parts = $rel -split '\\'
    -not ($parts | Where-Object { $excludeDirs -contains $_ })
} | ForEach-Object {
    $rel = $_.FullName.Substring($srcLen).TrimStart('\')
    $target = Join-Path $dst $rel
    $tdir = Split-Path $target -Parent
    if (-not (Test-Path $tdir)) { New-Item -ItemType Directory -Path $tdir -Force | Out-Null }
    Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    $copied++
}

# ---------------------------------------------------------------------------
# 2) Rename token-bearing DIRECTORIES (deepest first so child paths stay valid)
# ---------------------------------------------------------------------------
Get-ChildItem $dst -Recurse -Directory -Force |
    Sort-Object { $_.FullName.Length } -Descending |
    Where-Object { $_.Name -like "*$Token*" } |
    ForEach-Object {
        Rename-Item -LiteralPath $_.FullName -NewName ($_.Name.Replace($Token, $Name))
    }

# ---------------------------------------------------------------------------
# 3) Rename token-bearing FILES (after folder renames)
# ---------------------------------------------------------------------------
Get-ChildItem $dst -Recurse -File -Force |
    Where-Object { $_.Name -like "*$Token*" } |
    ForEach-Object {
        Rename-Item -LiteralPath $_.FullName -NewName ($_.Name.Replace($Token, $Name))
    }

# ---------------------------------------------------------------------------
# 4) Replace the token INSIDE text files
# ---------------------------------------------------------------------------
$textExt = @('.cs', '.csproj', '.user', '.slnx', '.sln', '.json', '.md', '.config',
    '.xml', '.props', '.targets', '.editorconfig', '.txt', '.resx', '.razor',
    '.cshtml', '.yml', '.yaml', '.manifest', '.settings')
$nameOnly = @('.gitignore', '.gitattributes', '.editorconfig', 'Dockerfile')
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Test-IsTextFile($file) {
    return ($textExt -contains $file.Extension.ToLower()) -or ($nameOnly -contains $file.Name)
}

$replaced = 0
Get-ChildItem $dst -Recurse -File -Force | ForEach-Object {
    if (Test-IsTextFile $_) {
        $content = [System.IO.File]::ReadAllText($_.FullName)
        if ($content.Contains($Token)) {
            [System.IO.File]::WriteAllText($_.FullName, $content.Replace($Token, $Name), $utf8NoBom)
            $replaced++
        }
    }
}

# ---------------------------------------------------------------------------
# 5) Safety: warn on any residual token (in content or in a path)
# ---------------------------------------------------------------------------
$residualFiles = @()
Get-ChildItem $dst -Recurse -File -Force | ForEach-Object {
    if ((Test-IsTextFile $_) -and ([System.IO.File]::ReadAllText($_.FullName)).Contains($Token)) {
        $residualFiles += $_.FullName
    }
}
$residualPaths = Get-ChildItem $dst -Recurse -Force |
    Where-Object { $_.Name -like "*$Token*" } |
    Select-Object -ExpandProperty FullName

Write-Host ""
Write-Host "Created add-on '$Name' from template:" -ForegroundColor Green
Write-Host "  template : $src"
Write-Host "  output   : $dst"
Write-Host "  files copied: $copied   files token-replaced: $replaced"

if ($residualFiles -or $residualPaths) {
    Write-Warning "Residual token '$Token' still present:"
    $residualFiles | ForEach-Object { Write-Host "   [content] $_" -ForegroundColor DarkYellow }
    $residualPaths | ForEach-Object { Write-Host "   [name]    $_" -ForegroundColor DarkYellow }
}
else {
    Write-Host "  no residual '$Token' remaining." -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 6) Optional build
# ---------------------------------------------------------------------------
if ($Build) {
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    $slnx = Join-Path $dst "$Name.slnx"
    if (-not $dotnet) {
        Write-Warning "dotnet CLI not found - skipping build. Open $Name.slnx in Visual Studio."
    }
    elseif (-not (Test-Path $slnx)) {
        Write-Warning "Solution file not found: $slnx"
    }
    else {
        Write-Host ""
        Write-Host "Building: dotnet build `"$slnx`"" -ForegroundColor Cyan
        & $dotnet build $slnx -v minimal --nologo
        if ($LASTEXITCODE -eq 0) { Write-Host "BUILD SUCCEEDED" -ForegroundColor Green }
        else { Write-Warning "BUILD FAILED (exit $LASTEXITCODE). See output above." }
    }
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Open $Name.slnx in Visual Studio (or run: dotnet build `"$dst\$Name.slnx`")."
Write-Host "  2. Customize per add-on:"
Write-Host "       - <Name>.Common\Constants\MenuConstants.cs  (menu UIDs)"
Write-Host "       - <Name>.Common\Constants\SapConstants.cs   (DebugConnectionString)"
Write-Host "  3. Add your logic: override the stubbed methods in App.cs (Loading, *_Event)."
Write-Host "  4. Start the SAP B1 client + log into a company, then F5 to attach."
