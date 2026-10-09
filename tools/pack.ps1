# Build a distributable package.
#
# Why NOT `dotnet publish`
# -----------------------
# This project is WinUI 3 with WindowsPackageType=None (unpackaged). Measured
# behaviour: `dotnet publish` does NOT emit the compiled XAML resources. It is
# missing these three files:
#     App.xbf / MainWindow.xbf   compiled XAML (LoadComponent loads these)
#     LanSound.pri               resource index
# Without them the app dies on launch (XamlParseException, exit code 0xC0000409).
# `dotnet build -c Release -r <rid>` output DOES contain them and is verified to run.
# publish additionally drags in unused AI libs (DirectML / onnxruntime).
#
# So we package the RID-specific **build** output.
#
# ASCII-only on purpose: Windows PowerShell reads .ps1 using the local code page
# and mangles non-ASCII literals, which breaks parsing.
#
# Usage:
#   powershell -File tools/pack.ps1
#   powershell -File tools/pack.ps1 -Rid win-arm64
param(
    [string]$Rid = "win-x64",
    [string]$Out = "",
    [switch]$KeepPdb
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $here ".."
$csproj = Join-Path $proj "pc-app\pc-app.csproj"

if (-not $Out) { $Out = Join-Path $here "..\dist\LanSound-$Rid" }
$Out = [System.IO.Path]::GetFullPath($Out)

Write-Host "== 1/4 build (Release, $Rid) =="
& dotnet build $csproj -c Release -r $Rid
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$buildOut = Join-Path $proj "pc-app\bin\Release\net8.0-windows10.0.19041.0\$Rid"
if (-not (Test-Path $buildOut)) { throw "build output not found: $buildOut" }

Write-Host "== 2/4 verify XAML resources (missing ones crash the app) =="
$required = @("LanSound.exe", "App.xbf", "MainWindow.xbf", "LanSound.pri", "Assets\LanSound.ico")
$missing = @()
foreach ($f in $required) {
    if (-not (Test-Path (Join-Path $buildOut $f))) { $missing += $f }
}
if ($missing.Count -gt 0) {
    throw ("build output is missing required files; the package would crash on launch:" + [Environment]::NewLine + "  " + ($missing -join ([Environment]::NewLine + "  ")))
}
Write-Host "  required files present (.xbf / .pri included)"

Write-Host "== 3/4 copy to $Out =="
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Path $Out -Force | Out-Null
$rcArgs = @($buildOut, $Out, "/MIR", "/NJH", "/NJS", "/NP", "/NDL", "/NC", "/NS")
if (-not $KeepPdb) { $rcArgs += @("/XF", "*.pdb") }
& robocopy @rcArgs | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed, code $LASTEXITCODE" }

# phone-web is NOT part of pc-app's build output (it lives outside the project),
# but the server locates it by walking up from the exe directory. Without it the
# phone page 404s and the log says "phone-web dir not found".
# Copy it next to the exe so the walk finds it.
$webSrc = Join-Path $proj "phone-web"
if (-not (Test-Path $webSrc)) { throw "phone-web not found: $webSrc" }
$webDst = Join-Path $Out "phone-web"
& robocopy $webSrc $webDst /MIR /NJH /NJS /NP /NDL /NC /NS /XF *.map | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy (phone-web) failed, code $LASTEXITCODE" }
Write-Host "  phone-web bundled ($((Get-ChildItem $webDst -File).Count) files)"

Write-Host "== 4/4 result =="
$files = Get-ChildItem $Out -Recurse -File
$size = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "  dir  : $Out"
Write-Host "  files: $($files.Count)   size: $size MB"
Write-Host ""
Write-Host "Ship the whole folder; users double-click LanSound.exe."
Write-Host "Requires Windows 10 1809+ / Windows 11 and the .NET 8 Desktop Runtime."
