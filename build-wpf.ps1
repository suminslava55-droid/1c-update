# Build 1C Update WPF
#   -Publish  : self-contained single-file exe (default output: wpf\1CUpdate.Wpf\publish\1CUpdate.exe)
#   -Folder   : self-contained folder (many files) instead of single-file
#   -Run      : start after build
param(
    [switch]$Publish,
    [switch]$Folder,
    [switch]$Run
)

$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "wpf\1CUpdate.Wpf\1CUpdate.Wpf.csproj"
$outDir = Join-Path $PSScriptRoot "wpf\1CUpdate.Wpf\publish"

# .NET 8 SDK may be installed per-user (dotnet-install) - look there first
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $dotnet = $cmd.Source }
}
if (-not $dotnet -or -not (Test-Path $dotnet)) {
    Write-Host "dotnet not found. Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}

Write-Host "SDK: $dotnet ($(& $dotnet --version))" -ForegroundColor Green

if ($Publish) {
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

    if ($Folder) {
        Write-Host "`nPublishing self-contained folder..." -ForegroundColor Cyan
        & $dotnet publish $proj -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=false -p:DebugType=none -o $outDir
    }
    else {
        Write-Host "`nPublishing self-contained single file..." -ForegroundColor Cyan
        & $dotnet publish $proj -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $outDir
    }

    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $exe = Join-Path $outDir "1CUpdate.exe"
    Write-Host "`nDone: $exe" -ForegroundColor Green
    if ($Run) { & $exe }
    return
}

Write-Host "`nBuilding Release..." -ForegroundColor Cyan
& $dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "`nDone: wpf\1CUpdate.Wpf\bin\Release\net8.0-windows\1CUpdate.exe" -ForegroundColor Green

if ($Run) {
    & $dotnet (Join-Path $PSScriptRoot "wpf\1CUpdate.Wpf\bin\Release\net8.0-windows\1CUpdate.dll")
}
