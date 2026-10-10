<#
=============================================================================
 build.ps1 -- maimai DX (Sinmai / SDEZ) MelonLoader mod build script

 IMPORTANT: this file is intentionally ASCII-only.
 Windows PowerShell 5.1 decodes a BOM-less script using the system ANSI
 codepage (GBK on a Chinese system). Any non-ASCII character in this file can
 swallow the following ASCII char and silently comment out the next line.
 Keeping it pure ASCII makes it immune to that, no matter which tool rewrites it.

 Usage:
   .\build.ps1
   .\build.ps1 -Deploy
   .\build.ps1 -GameRoot "X:\path\to\SDEZ170\Package" -Deploy
=============================================================================
#>
[CmdletBinding()]
param(
    # Game Package dir (contains Sinmai.exe / Sinmai_Data / MelonLoader / Mods)
    [string]$GameRoot = (Join-Path $PSScriptRoot '..\SDEZ170\Package'),

    # Output assembly name
    [string]$AssemblyName = 'MaimaiGhostReplay',

    # Copy the built dll into Mods\ after a successful build
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- paths
$GameRoot = (Resolve-Path $GameRoot).Path
$Managed  = Join-Path $GameRoot 'Sinmai_Data\Managed'
$Melon    = Join-Path $GameRoot 'MelonLoader\net35'
$ModsDir  = Join-Path $GameRoot 'Mods'

foreach ($p in @($Managed, $Melon)) {
    if (-not (Test-Path $p)) { throw "Missing directory: $p  (pass -GameRoot)" }
}

# ---------------------------------------------------------------- compiler
# .NET Framework 4.0 csc = C# 5 only. No string interpolation / ?. / nameof.
$csc = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $csc) { throw "csc.exe not found. Install .NET Framework 4.x or use dotnet build." }
Write-Host "Compiler: $csc" -ForegroundColor Cyan

# ---------------------------------------------------------------- references
# NOTE: do NOT reference Managed\System.Core.dll / System.Xml.dll / mscorlib.dll.
# csc already pulls the framework copies and duplicates raise CS1703.
$refs = @(
    (Join-Path $Managed 'Assembly-CSharp.dll'),
    (Join-Path $Managed 'Assembly-CSharp-firstpass.dll'),
    (Join-Path $Managed 'UnityEngine.dll'),
    (Join-Path $Managed 'UnityEngine.CoreModule.dll'),
    (Join-Path $Managed 'UnityEngine.UI.dll'),
    (Join-Path $Managed 'UnityEngine.IMGUIModule.dll'),
    (Join-Path $Managed 'UnityEngine.AudioModule.dll'),
    (Join-Path $Managed 'UnityEngine.AnimationModule.dll'),
    (Join-Path $Managed 'UnityEngine.AssetBundleModule.dll'),
    (Join-Path $Managed 'UnityEngine.ImageConversionModule.dll'),
    (Join-Path $Managed 'UnityEngine.UnityWebRequestModule.dll'),
    (Join-Path $Managed 'UnityEngine.VideoModule.dll'),
    (Join-Path $Managed 'UnityEngine.TextRenderingModule.dll'),
    (Join-Path $Managed 'UnityEngine.UIModule.dll'),
    (Join-Path $Managed 'UnityEngine.ParticleSystemModule.dll'),
    (Join-Path $Managed 'UnityEngine.Timeline.dll'),
    (Join-Path $Managed 'AMDaemon.NET.dll'),
    (Join-Path $Managed 'Unity.TextMeshPro.dll'),
    # Mod loader (bundled versions; do not use NuGet Lib.Harmony - version mismatch)
    (Join-Path $Melon 'MelonLoader.dll'),
    (Join-Path $Melon '0Harmony.dll')
) | Where-Object { Test-Path $_ }

Write-Host "Referencing $($refs.Count) assemblies" -ForegroundColor Cyan

# ---------------------------------------------------------------- sources
$sources = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs -Recurse |
           Select-Object -ExpandProperty FullName
if (-not $sources) { throw "No .cs files under src\" }

# csc.exe (non-Roslyn) decodes BOM-less UTF-8 sources as system ANSI (GBK),
# which corrupts non-ASCII comments and can emit:
#   fatal error CS2021: file name "..." is too long or invalid
# Re-save any source that is missing a UTF-8 BOM.
$patched = 0
foreach ($f in $sources) {
    $bytes = [System.IO.File]::ReadAllBytes($f)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { continue }
    $text = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)
    $utf8Bom = New-Object System.Text.UTF8Encoding -ArgumentList ([bool]$true)
    [System.IO.File]::WriteAllText($f, $text, $utf8Bom)
    $patched++
}
if ($patched -gt 0) { Write-Host "Added UTF-8 BOM to $patched source file(s)" -ForegroundColor Yellow }

$outDir = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outDll = Join-Path $outDir "$AssemblyName.dll"

# ---------------------------------------------------------------- compile
$cscArgs = @(
    '/nologo', '/target:library', '/platform:x64', '/optimize+', '/debug-'
    "/out:$outDll"
) + ($refs | ForEach-Object { "/reference:$_" }) + $sources

Write-Host "`n--- csc ---" -ForegroundColor DarkGray
$log = Join-Path $outDir 'csc.log'
$cscOut = & $csc @cscArgs 2>&1
$code = $LASTEXITCODE
$cscOut | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $log
if ($code -ne 0) { throw "Build failed (exit $code), see $log" }

Write-Host "`n[OK] built: $outDll" -ForegroundColor Green
Write-Host ("     size: {0:N0} bytes" -f (Get-Item $outDll).Length)

# ---------------------------------------------------------------- deploy
if ($Deploy) {
    if (-not (Test-Path $ModsDir)) { New-Item -ItemType Directory -Force -Path $ModsDir | Out-Null }
    Copy-Item $outDll $ModsDir -Force
    Write-Host "[OK] deployed to: $(Join-Path $ModsDir "$AssemblyName.dll")" -ForegroundColor Green
}
else {
    Write-Host "`nCopy the dll to: $ModsDir   (or re-run with -Deploy)" -ForegroundColor Yellow
}
