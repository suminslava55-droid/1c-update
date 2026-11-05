# Сборка 1CUpdate.exe
param(
    [switch]$NoIcon,
    [switch]$Run,
    [string]$PsExec = '',
    [string]$ScriptFile = '',
    [string]$PsExec64 = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'Не найден csc.exe (.NET Framework 4.x).' }

$src = Join-Path $root 'src\Update1C.cs'
$out = Join-Path $root '1CUpdate.exe'
$manifest = Join-Path $root 'app.manifest'
$icon = Join-Path $root 'app.ico'

if (-not (Test-Path $src)) { throw "Не найден исходник: $src" }

if (-not $NoIcon -and -not (Test-Path $icon)) {
    & (Join-Path $root 'make-icon.ps1') -Path $icon | Out-Host
}

$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.DirectoryServices.dll'
)

# --- встроенные ресурсы ---
$scriptRes = if ($ScriptFile) { $ScriptFile } else { Join-Path $root 'my\1C8InstallAndUninstall-1.09.ps1' }
if (-not (Test-Path $scriptRes)) { throw "Не найден скрипт для встраивания: $scriptRes" }
$resources = @("/resource:$scriptRes,script.ps1")
Write-Host ("Встраиваю скрипт: {0}" -f $scriptRes) -ForegroundColor DarkGray

# ищем PsExec: явный параметр -> папка my -> папка сборки -> C:\inst
function Find-Tool([string]$explicit, [string]$name) {
    if ($explicit) { return $explicit }
    foreach ($dir in @((Join-Path $root 'my'), $root, 'C:\inst')) {
        $candidate = Join-Path $dir $name
        if (Test-Path $candidate) { return $candidate }
    }
    return ''
}
$PsExec   = Find-Tool $PsExec   'PsExec.exe'
$PsExec64 = Find-Tool $PsExec64 'PsExec64.exe'

$embeddedTools = 0
if ($PsExec -and (Test-Path $PsExec)) {
    $resources += "/resource:$PsExec,psexec.exe"
    Write-Host ("Встраиваю PsExec (x86): {0}" -f $PsExec) -ForegroundColor DarkGray
    $embeddedTools++
}
if ($PsExec64 -and (Test-Path $PsExec64)) {
    $resources += "/resource:$PsExec64,psexec64.exe"
    Write-Host ("Встраиваю PsExec (x64): {0}" -f $PsExec64) -ForegroundColor DarkGray
    $embeddedTools++
}
if ($embeddedTools -eq 0) {
    Write-Host 'PsExec НЕ встроен (положите PsExec.exe / PsExec64.exe в папку my или укажите -PsExec)' -ForegroundColor Yellow
}

$argsList = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    '/nowarn:1591',
    "/out:$out",
    "/win32manifest:$manifest"
) + $refs + $resources + @($src)

if ((Test-Path $icon) -and -not $NoIcon) { $argsList += "/win32icon:$icon" }

# если exe запущен, файл занят — отложим его в сторону, соберём новый
if (Test-Path $out) {
    try {
        $fs = [System.IO.File]::Open($out, 'Open', 'ReadWrite', 'None')
        $fs.Close()
    } catch {
        $old = [System.IO.Path]::ChangeExtension($out, $null).TrimEnd('.') + '.old.exe'
        $i = 1
        while (Test-Path $old) { $old = [System.IO.Path]::ChangeExtension($out, $null).TrimEnd('.') + ".old$i.exe"; $i++ }
        Move-Item $out $old -Force
        Write-Host ("Запущенный exe переименован в {0} — новый запуск возьмёт свежую сборку" -f (Split-Path $old -Leaf)) -ForegroundColor Yellow
    }
}

Write-Host 'Компиляция...' -ForegroundColor Cyan
& $csc $argsList
if ($LASTEXITCODE -ne 0) { throw "Ошибка компиляции (код $LASTEXITCODE)" }

$item = Get-Item $out
Write-Host ("Готово: {0}  ({1:N0} байт)" -f $item.FullName, $item.Length) -ForegroundColor Green

if ($Run) { Start-Process $out }
