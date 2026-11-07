# Подпись 1CUpdate.exe сертификатом code signing.
#
# Примеры:
#   .\sign.ps1 -File .\wpf\1CUpdate.Wpf\publish\1CUpdate.exe -Pfx C:\certs\my.pfx -Password Secret
#   .\sign.ps1 -File .\1CUpdate.exe -Thumbprint 0123456789ABCDEF...
#   .\sign.ps1 -List                     # показать сертификаты в хранилище
#
# Нужен signtool.exe из Windows SDK (Windows SDK -> "Signing Tools for Desktop Apps").
param(
    [string]$File,
    [string]$Pfx,
    [string]$Password,
    [string]$Thumbprint,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$List
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $kits = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kits) {
        $candidate = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    return $null
}

if ($List -or (-not $File)) {
    Write-Host "Сертификаты code signing в хранилище текущего пользователя:" -ForegroundColor Cyan
    $certs = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue
    if (-not $certs) {
        Write-Host "  (нет)" -ForegroundColor Yellow
        Write-Host "`nКупить сертификат: https://learn.microsoft.com/windows-hardware/drivers/dashboard/" -ForegroundColor Yellow
    }
    else {
        $certs | Select-Object Subject, Thumbprint, NotAfter | Format-Table -AutoSize
    }
    exit 0
}

if (-not (Test-Path $File)) {
    Write-Host "Файл не найден: $File" -ForegroundColor Red
    exit 1
}

$signtool = Find-SignTool
if (-not $signtool) {
    Write-Host "signtool.exe не найден." -ForegroundColor Red
    Write-Host "Установите Windows SDK (компонент 'Signing Tools for Desktop Apps'):" -ForegroundColor Yellow
    Write-Host "  https://developer.microsoft.com/windows/downloads/windows-sdk/" -ForegroundColor Yellow
    exit 1
}

Write-Host "signtool: $signtool" -ForegroundColor Green

if ($Pfx) {
    if (-not (Test-Path $Pfx)) { Write-Host "PFX не найден: $Pfx" -ForegroundColor Red; exit 1 }
    & $signtool sign /f $Pfx /p $Password /fd sha256 /tr $TimestampUrl /td sha256 $File
}
elseif ($Thumbprint) {
    & $signtool sign /sha1 $Thumbprint /fd sha256 /tr $TimestampUrl /td sha256 $File
}
else {
    Write-Host "Укажите -Pfx <файл> -Password <пароль> или -Thumbprint <отпечаток>." -ForegroundColor Yellow
    Write-Host "Список сертификатов: .\sign.ps1 -List" -ForegroundColor Yellow
    exit 1
}

if ($LASTEXITCODE -ne 0) { Write-Host "Подпись не удалась." -ForegroundColor Red; exit $LASTEXITCODE }

$sig = Get-AuthenticodeSignature $File
Write-Host "`nСтатус подписи: $($sig.Status)" -ForegroundColor Green
if ($sig.SignerCertificate) {
    Write-Host "Издатель: $($sig.SignerCertificate.Subject)"
}
