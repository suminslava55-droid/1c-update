# Имитация psexec для автотеста GUI: получает те же аргументы,
# что и настоящий psexec, и пишет журнал в каталог -dl.
$pc = [string]$args[0]
$pc = $pc.TrimStart('\')
$dl = ''
for ($i = 0; $i -lt $args.Count; $i++) {
    if ($args[$i] -eq '-dl') { $dl = [string]$args[$i + 1] }
}
if ($dl -eq '' -or $pc -eq '') { Write-Error 'нет -dl или имени ПК'; exit 9 }

$log = Join-Path $dl ($pc + '.log')
function W([string]$s) {
    $line = ('{0} {1}' -f (Get-Date -Format 'yyyy.MM.dd HH:mm:ss'), $s) + "`r`n"
    [System.IO.File]::AppendAllText($log, $line, [System.Text.Encoding]::Unicode)
}

W '---------------------------------------------------------------------------------'
W 'Версия скрипта: 1.08'
W 'Параметры запуска скрипта: -dd ... -dl ...'
W 'Начало работы скрипта'
Start-Sleep -Milliseconds 1000
W 'Установка 1С:Предприятие, версия 8.3.22.2239'
Start-Sleep -Milliseconds 1500

if ($pc -match 'BAD') {
    W 'Не удалось получить доступ к каталогу с дистрибутивами 1С, проверьте путь и права доступа'
    exit 2
}

W 'После установки проверка: платформа найдена'
Start-Sleep -Milliseconds 700
W 'Окончание работы скрипта'
Write-Output ("PsExec fake: {0} — готово" -f $pc)
exit 0
