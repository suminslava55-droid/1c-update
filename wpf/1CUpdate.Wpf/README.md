# 1C Update WPF

Современный интерфейс (WPF, .NET 8) для массового обновления платформы 1С на компьютерах домена.
Тёмная тема и все стили взяты из шаблона **WpfUiKit** (`Themes/Dark.xaml`).

![Окно 1C Update WPF](screenshot-wpf.png)

## Что внутри

- **Шапка**: логотип, статус, количество ПК, число потоков, «Проверить пути».
- **Левая панель**: список ПК (вручную / из файла / из Active Directory), все параметры установки.
- **KPI-карточки**: всего ПК, успешно, ошибки, в процессе — обновляются вживую.
- **Вкладки**: «Результаты» (таблица со цветными статусами) и «Журнал» (цветной вывод).
- **Панель выполнения**: Выполнить (F5), Стоп (F6), очистка вывода, отчёт, прогресс.
- **Тёмный заголовок окна** через `DwmSetWindowAttribute`.

Функциональность полностью повторяет WinForms-версию:

- запуск `psexec` параллельно (1–64 потоков), чтение вывода и файла прогресса;
- проверка «версия уже стоит» до установки и подтверждение версии после;
- три способа доставки скрипта (встроенный exe / .ps1 на шаре / внешний .ps1);
- режимы `--remote-worker` и `--commands` (тот же exe работает на целевом ПК);
- настройки в `%APPDATA%\1CUpdate\settings.ini` (общие с WinForms-версией);
- отчёт CSV/TXT.

## Сборка

Нужен **.NET 8 SDK**. На этой машине он установлен локально в профиль:
`C:\Users\User\.dotnet\dotnet.exe` (8.0.427).

```powershell
# обычная сборка (bin\Release, запуск через dotnet)
powershell -ExecutionPolicy Bypass -File ..\..\build-wpf.ps1

# ОДИН самодостаточный файл (~64 МБ) -> publish\1CUpdate.exe
powershell -ExecutionPolicy Bypass -File ..\..\build-wpf.ps1 -Publish

# папка (много файлов, ~146 МБ) вместо single-file
powershell -ExecutionPolicy Bypass -File ..\..\build-wpf.ps1 -Publish -Folder
```

Или напрямую:

```powershell
cd wpf\1CUpdate.Wpf
& "$env:USERPROFILE\.dotnet\dotnet.exe" publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o publish
```

### Что внутри одного файла

`publish\1CUpdate.exe` (~64 МБ) содержит всё:

- WPF-приложение и рантайм .NET 8 (self-contained — .NET на ПК не нужен);
- встроенный скрипт `1C8InstallAndUninstall.ps1`;
- PsExec x86/x64;
- standalone-воркер (WinForms `1CUpdate.exe`, 1084 КБ) — именно он копируется
  через `psexec -c` на целевые ПК, поэтому режим «копировать exe на ПК» работает,
  хотя сама WPF-сборка состоит из папки.

Нюанс single-file: при первом запуске нативные библиотеки распаковываются в
`%TEMP%\.net\1CUpdate`. Если на машине запрещена запись в `%TEMP%`, используйте
сборку папкой (`-Publish -Folder`).

Если Windows показывает SmartScreen («Система Windows защитила ваш компьютер») —
это следствие неподписанного exe и метки «скачано из интернета». Как убрать —
см. корневой `README.md`, раздел «SmartScreen и подпись» (`sign.ps1`).

## Структура

```
1CUpdate.Wpf/
├─ App.xaml / App.xaml.cs        — старт, режимы --remote-worker и --commands
├─ MainWindow.xaml(.cs)          — главное окно
├─ app.manifest                  — DPI awareness, asInvoker
├─ Themes/Dark.xaml              — палитра и стили (из WpfUiKit)
├─ Common/                       — ObservableObject, RelayCommand, тёмный title bar
├─ Converters/                   — Hex→Brush, Bool/Null→Visibility
├─ Models/                       — Job, Options, KpiItem, LogEntry
├─ Services/                     — Runner, RemoteWorker, LogTail, Embedded, Settings, VersionScanner
├─ ViewModels/MainViewModel.cs   — вся логика окна
├─ Views/AdDialog.xaml(.cs)      — выбор компьютеров из Active Directory
├─ Assets/app.ico
└─ publish/                      — готовая self-contained сборка: один файл 1CUpdate.exe (~64 МБ)
```

## Отличия от WinForms-версии

- MVVM: привязки, команды, `INotifyPropertyChanged` вместо ручного обновления контролов.
- Сглаженные закругления и анимации наведения — средствами WPF (`CornerRadius`, триггеры).
- Журнал — виртуализированный список с цветными строками (быстрее `RichTextBox` при большом выводе).
- Вкладки «Результаты / Журнал» вместо вертикального разделения.
