using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using OneCUpdate.Common;
using OneCUpdate.Models;
using OneCUpdate.Services;

namespace OneCUpdate.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher;
    private readonly Settings _settings;
    private readonly DispatcherTimer _clock;

    private Runner _runner;
    private List<Job> _jobs = new List<Job>();
    private DateTime _startedAll;
    private bool _isRunning;

    // --- поля настроек ---
    private string _computers = "";
    private string _distribDir = "";
    private string _logDir = "";
    private string _scriptPath = "";
    private string _psexecPath = "";
    private string _user = "";
    private string _password = "";
    private bool _useEmbeddedPsexec = true;
    private int _threads = 4;

    private bool _installNo;
    private bool _installLast = true;
    private bool _installVersionSelected;
    private string _installVersion = "";

    private bool _deleteNo = true;
    private bool _deleteAel;
    private bool _deleteAll;
    private bool _deleteVersionSelected;
    private string _deleteVersion = "";

    private bool _designerAllClients;
    private bool _thinClient = true;
    private bool _thinClientFile;
    private int _deliveryIndex;
    private bool _dryRun;

    private string _statusText = "Готов";
    private int _progress;
    private int _progressMax = 100;
    private string _progressText = "";
    private string _checkSummary = "";

    public MainViewModel()
    {
        _dispatcher = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
        _settings = Settings.Load();

        KpiTotal = new KpiItem { Label = "Всего ПК", Value = "0", Accent = "#78B4F0" };
        KpiSuccess = new KpiItem { Label = "Успешно", Value = "0", Accent = "#89D185" };
        KpiFailed = new KpiItem { Label = "Ошибки", Value = "0", Accent = "#F48771" };
        KpiRunning = new KpiItem { Label = "В процессе", Value = "0", Accent = "#007ACC" };

        KpiItems.Add(KpiTotal);
        KpiItems.Add(KpiSuccess);
        KpiItems.Add(KpiFailed);
        KpiItems.Add(KpiRunning);

        RunCommand = new RelayCommand(StartRun, () => !IsRunning);
        StopCommand = new RelayCommand(StopRun, () => IsRunning);
        CheckPathsCommand = new RelayCommand(() => CheckPaths(true), () => !IsRunning);
        ClearLogCommand = new RelayCommand(() => LogEntries.Clear());
        SaveReportCommand = new RelayCommand(SaveReport, () => _jobs.Count > 0);
        LoadFromFileCommand = new RelayCommand(LoadFromFile);
        ClearComputersCommand = new RelayCommand(() => Computers = "");

        LoadSettings();

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (s, e) => Tick();
        _clock.Start();
    }

    // ===================== Свойства =====================

    public ObservableCollection<Job> Jobs { get; } = new ObservableCollection<Job>();
    public ObservableCollection<KpiItem> KpiItems { get; } = new ObservableCollection<KpiItem>();
    public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();
    public ObservableCollection<string> AvailableVersions { get; } = new ObservableCollection<string>();

    public KpiItem KpiTotal { get; }
    public KpiItem KpiSuccess { get; }
    public KpiItem KpiFailed { get; }
    public KpiItem KpiRunning { get; }

    public RelayCommand RunCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand CheckPathsCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand SaveReportCommand { get; }
    public RelayCommand LoadFromFileCommand { get; }
    public RelayCommand ClearComputersCommand { get; }

    public string Computers
    {
        get => _computers;
        set
        {
            if (Set(ref _computers, value)) OnPropertyChanged(nameof(ComputerCountText));
        }
    }

    public string ComputerCountText => "ПК: " + ParseComputers().Count;

    public string DistribDir
    {
        get => _distribDir;
        set => Set(ref _distribDir, value);
    }

    public string LogDir
    {
        get => _logDir;
        set => Set(ref _logDir, value);
    }

    public string ScriptPath
    {
        get => _scriptPath;
        set => Set(ref _scriptPath, value);
    }

    public string PsexecPath
    {
        get => _psexecPath;
        set => Set(ref _psexecPath, value);
    }

    public string User
    {
        get => _user;
        set => Set(ref _user, value);
    }

    public string Password
    {
        get => _password;
        set => Set(ref _password, value);
    }

    public bool UseEmbeddedPsexec
    {
        get => _useEmbeddedPsexec;
        set
        {
            if (Set(ref _useEmbeddedPsexec, value)) OnPropertyChanged(nameof(PsexecPathEditable));
        }
    }

    public bool PsexecPathEditable => !UseEmbeddedPsexec;

    public int Threads
    {
        get => _threads;
        set
        {
            int v = Math.Max(1, Math.Min(64, value));
            if (Set(ref _threads, v)) OnPropertyChanged(nameof(ThreadsText));
        }
    }

    public string ThreadsText
    {
        get => Threads.ToString();
        set
        {
            int n;
            if (int.TryParse(value, out n)) Threads = n;
        }
    }

    public bool InstallNo
    {
        get => _installNo;
        set { if (value) { _installNo = true; _installLast = false; _installVersionSelected = false; RaiseInstall(); } }
    }

    public bool InstallLast
    {
        get => _installLast;
        set { if (value) { _installNo = false; _installLast = true; _installVersionSelected = false; RaiseInstall(); } }
    }

    public bool InstallVersionSelected
    {
        get => _installVersionSelected;
        set { if (value) { _installNo = false; _installLast = false; _installVersionSelected = true; RaiseInstall(); } }
    }

    public string InstallVersion
    {
        get => _installVersion;
        set => Set(ref _installVersion, value);
    }

    private void RaiseInstall()
    {
        OnPropertyChanged(nameof(InstallNo));
        OnPropertyChanged(nameof(InstallLast));
        OnPropertyChanged(nameof(InstallVersionSelected));
    }

    public bool DeleteNo
    {
        get => _deleteNo;
        set { if (value) { _deleteNo = true; _deleteAel = false; _deleteAll = false; _deleteVersionSelected = false; RaiseDelete(); } }
    }

    public bool DeleteAel
    {
        get => _deleteAel;
        set { if (value) { _deleteNo = false; _deleteAel = true; _deleteAll = false; _deleteVersionSelected = false; RaiseDelete(); } }
    }

    public bool DeleteAll
    {
        get => _deleteAll;
        set { if (value) { _deleteNo = false; _deleteAel = false; _deleteAll = true; _deleteVersionSelected = false; RaiseDelete(); } }
    }

    public bool DeleteVersionSelected
    {
        get => _deleteVersionSelected;
        set { if (value) { _deleteNo = false; _deleteAel = false; _deleteAll = false; _deleteVersionSelected = true; RaiseDelete(); } }
    }

    public string DeleteVersion
    {
        get => _deleteVersion;
        set => Set(ref _deleteVersion, value);
    }

    private void RaiseDelete()
    {
        OnPropertyChanged(nameof(DeleteNo));
        OnPropertyChanged(nameof(DeleteAel));
        OnPropertyChanged(nameof(DeleteAll));
        OnPropertyChanged(nameof(DeleteVersionSelected));
    }

    public bool DesignerAllClients
    {
        get => _designerAllClients;
        set
        {
            if (!Set(ref _designerAllClients, value)) return;
            if (value)
            {
                _thinClient = true;
                _thinClientFile = true;
                OnPropertyChanged(nameof(ThinClient));
                OnPropertyChanged(nameof(ThinClientFile));
            }
            OnPropertyChanged(nameof(ComponentsEditable));
        }
    }

    public bool ThinClient
    {
        get => _thinClient;
        set => Set(ref _thinClient, value);
    }

    public bool ThinClientFile
    {
        get => _thinClientFile;
        set => Set(ref _thinClientFile, value);
    }

    public bool ComponentsEditable => !DesignerAllClients;

    public int DeliveryIndex
    {
        get => _deliveryIndex;
        set
        {
            if (Set(ref _deliveryIndex, value)) OnPropertyChanged(nameof(ScriptPathEditable));
        }
    }

    public bool ScriptPathEditable => DeliveryIndex != 0;

    public bool DryRun
    {
        get => _dryRun;
        set => Set(ref _dryRun, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            RunCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            CheckPathsCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(NotRunning));
        }
    }

    public bool NotRunning => !IsRunning;

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public string CheckSummary
    {
        get => _checkSummary;
        set { if (Set(ref _checkSummary, value)) OnPropertyChanged(nameof(HasCheckSummary)); }
    }

    public bool HasCheckSummary => !string.IsNullOrEmpty(_checkSummary);

    public int Progress
    {
        get => _progress;
        set => Set(ref _progress, value);
    }

    public int ProgressMax
    {
        get => _progressMax;
        set => Set(ref _progressMax, Math.Max(1, value));
    }

    public string ProgressText
    {
        get => _progressText;
        set => Set(ref _progressText, value);
    }

    public string DeliveryMode => DeliveryIndex == 1 ? "share" : DeliveryIndex == 2 ? "path" : "exe";

    public string InstallMode => InstallNo ? "no" : InstallLast ? "last" : InstallVersion.Trim();

    public string DeleteMode => DeleteNo ? "no" : DeleteAel ? "ael" : DeleteAll ? "all" : DeleteVersion.Trim();

    // ===================== Компьютеры =====================

    public List<string> ParseComputers()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string text = (Computers ?? "").Replace("\r", "\n");
        string[] parts = text.Split(new[] { '\n', ';', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string raw in parts)
        {
            string name = raw.Trim().Trim('"').Trim('\'').Trim();
            if (name.Length == 0) continue;
            if (name.StartsWith("\\\\")) name = name.Substring(2);
            if (name.Length == 0) continue;
            if (seen.Add(name)) result.Add(name);
        }
        return result;
    }

    public void AddComputers(IEnumerable<string> names)
    {
        var existing = ParseComputers();
        var sb = new StringBuilder();
        foreach (string pc in existing) sb.AppendLine(pc);
        foreach (string pc in names)
        {
            if (!existing.Contains(pc, StringComparer.OrdinalIgnoreCase)) sb.AppendLine(pc);
        }
        Computers = sb.ToString().TrimEnd();
    }

    private void LoadFromFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Список компьютеров",
            Filter = "Текст (*.txt)|*.txt|Все файлы (*.*)|*.*"
        };
        try { if (File.Exists(@"C:\inst\comp.txt")) dlg.FileName = @"C:\inst\comp.txt"; } catch { }
        if (dlg.ShowDialog() != true) return;

        try
        {
            string[] lines = File.ReadAllLines(dlg.FileName, VersionScanner.DetectEncoding(dlg.FileName));
            var sb = new StringBuilder();
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith(";") || t.StartsWith("#")) continue;
                sb.AppendLine(t);
            }
            Computers = sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Warn("Не удалось прочитать файл:\n" + ex.Message);
        }
    }

    // ===================== Параметры =====================

    public Options CollectOptions()
    {
        var o = new Options
        {
            Psexec = PsexecPath.Trim(),
            Script = ScriptPath.Trim(),
            DistribDir = DistribDir.Trim(),
            LogDir = LogDir.Trim(),
            User = User.Trim(),
            Password = Password,
            Threads = Threads,
            DesignerAllClients = DesignerAllClients,
            ThinClient = ThinClient,
            ThinClientFile = ThinClientFile,
            DryRun = DryRun,
            Delivery = DeliveryMode
        };

        o.InstallMode = InstallMode;
        o.DeleteMode = DeleteMode;
        return o;
    }

    /// <summary>
    /// Для режима «копировать exe на ПК» psexec копирует ОДИН файл.
    /// WPF-сборка — это папка, поэтому берём вшитый standalone-воркер
    /// (WinForms exe со встроенным скриптом и PsExec) и распаковываем его.
    /// </summary>
    private string ResolveLocalExe()
    {
        if (Embedded.HasWorker)
        {
            try
            {
                string dir = Path.Combine(TempArea.Dir, "worker");
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, "1CUpdate.exe");
                byte[] data = Embedded.WorkerBytes;
                if (!Embedded.SameContent(target, data)) File.WriteAllBytes(target, data);
                return target;
            }
            catch (Exception ex)
            {
                AddLog(Palette.Warn, "Не удалось распаковать встроенный воркер: " + ex.Message);
            }
        }

        // запасной вариант — текущий exe (сработает только для single-file сборки)
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule.FileName;
    }

    private string ResolvePsexec(out string error)
    {
        error = null;
        if (UseEmbeddedPsexec && Embedded.HasPsexec)
        {
            byte[] data = Embedded.PsexecBytes;
            try
            {
                string target = Path.Combine(TempArea.Dir, Embedded.PsexecName);
                if (!Embedded.SameContent(target, data)) File.WriteAllBytes(target, data);
                return target;
            }
            catch (Exception ex)
            {
                error = "Не удалось распаковать встроенный PsExec.exe: " + ex.Message;
                return null;
            }
        }

        string path = PsexecPath.Trim();
        if (path.Length == 0)
        {
            error = "Не найден PsExec.exe: укажите путь в поле «PsExec.exe».";
            return null;
        }
        return path;
    }

    private string DeployScript(Options o, out string error)
    {
        error = null;
        if (DeliveryMode != "share") return o.Script;
        if (!Embedded.HasScript)
        {
            error = "В этой сборке нет встроенного скрипта — укажите .ps1 вручную.";
            return null;
        }

        byte[] data = Embedded.ScriptBytes;
        string target = o.Script;
        if (string.IsNullOrEmpty(target))
        {
            error = "Не задан путь, куда выложить встроенный скрипт.";
            return null;
        }
        try
        {
            string dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (Embedded.SameContent(target, data))
                AddLog(Palette.Muted, "Встроенный скрипт " + Embedded.ScriptVersion + " уже лежит на шаре: " + target);
            else
            {
                File.WriteAllBytes(target, data);
                AddLog(Palette.Ok, "Встроенный скрипт " + Embedded.ScriptVersion + " выложен на шару: " + target);
            }
            return target;
        }
        catch (Exception ex)
        {
            error = "Не удалось выложить встроенный скрипт в " + target + ": " + ex.Message +
                    "\n\nУкажите каталог, куда есть доступ на запись, либо выберите другой способ доставки скрипта.";
            return null;
        }
    }

    // ===================== Проверка путей =====================

    public bool CheckPaths(bool showResult)
    {
        var problems = new List<string>();
        var ok = new List<string>();
        Options o = CollectOptions();

        try
        {
            if (string.IsNullOrEmpty(o.DistribDir) || !Directory.Exists(o.DistribDir))
                problems.Add("Каталог дистрибутивов недоступен: " + o.DistribDir);
            else
            {
                var versions = VersionScanner.ScanVersions(o.DistribDir);
                ok.Add("Дистрибутивы: " + versions.Count + " версий (" +
                       string.Join(", ", versions.Take(6).ToArray()) + (versions.Count > 6 ? "…" : "") + ")");
                FillVersions(versions);
            }
        }
        catch (Exception ex) { problems.Add("Дистрибутивы: " + ex.Message); }

        try
        {
            if (DeliveryMode == "exe")
                ok.Add("Скрипт " + Embedded.ScriptVersion + " встроен в exe — psexec скопирует его на каждый ПК");
            else if (DeliveryMode == "share")
            {
                string scriptDir = Path.GetDirectoryName(o.Script);
                if (string.IsNullOrEmpty(scriptDir)) problems.Add("Не задан путь, куда выложить встроенный скрипт");
                else if (!VersionScanner.CanWriteTo(scriptDir)) problems.Add("Нет доступа на запись в каталог скрипта: " + scriptDir);
                else ok.Add("Встроенный скрипт " + Embedded.ScriptVersion + " будет выложен: " + o.Script);
            }
            else if (string.IsNullOrEmpty(o.Script) || !File.Exists(o.Script))
                problems.Add("Скрипт не найден: " + o.Script);
            else ok.Add("Скрипт найден: " + o.Script);
        }
        catch (Exception ex) { problems.Add("Скрипт: " + ex.Message); }

        if (DeliveryMode == "exe")
        {
            if (Embedded.HasWorker)
                ok.Add("Воркер для копирования на ПК встроен (" + (Embedded.WorkerBytes.Length / 1024) + " КБ)");
            else
                problems.Add("В сборке нет вшитого воркера — режим «копировать exe» не сработает");
        }

        try
        {
            if (UseEmbeddedPsexec && Embedded.HasPsexec)
                ok.Add("PsExec встроен в программу (распакуется автоматически)");
            else if (string.IsNullOrEmpty(o.Psexec) || !File.Exists(o.Psexec))
                problems.Add("PsExec.exe не найден: " + o.Psexec);
            else ok.Add("PsExec найден: " + o.Psexec);
        }
        catch (Exception ex) { problems.Add("PsExec: " + ex.Message); }

        StatusText = problems.Count == 0 ? "Проверка путей: всё на месте" : "Проверка путей: проблем — " + problems.Count;

        if (!showResult) return problems.Count == 0;

        foreach (string s in ok) AddLog(Palette.Ok, "✓ " + s);
        foreach (string s in problems) AddLog(Palette.Err, "✗ " + s);
        CheckSummary = problems.Count == 0
            ? "Проверка путей: всё на месте"
            : "Проверка путей: проблем — " + problems.Count + " (подробности в журнале)";
        return problems.Count == 0;
    }

    private void FillVersions(List<string> versions)
    {
        string current = InstallVersion;
        string currentDel = DeleteVersion;
        AvailableVersions.Clear();
        foreach (string v in versions) AvailableVersions.Add(v);
        if (string.IsNullOrEmpty(current) && versions.Count > 0) InstallVersion = versions[0];
        if (!string.IsNullOrEmpty(currentDel)) DeleteVersion = currentDel;
    }

    // ===================== Запуск =====================

    private void StartRun()
    {
        if (IsRunning) return;

        List<string> pcs = ParseComputers();
        if (pcs.Count == 0)
        {
            Warn("Добавьте компьютеры: по одному имени в строке, или кнопкой «Из файла…».");
            return;
        }

        Options o = CollectOptions();
        o.LocalExe = ResolveLocalExe();
        o.Delivery = DeliveryMode;

        var problems = new List<string>();

        if (!DryRun)
        {
            string resolveError;
            string psexec = ResolvePsexec(out resolveError);
            if (psexec == null) { Warn(resolveError); return; }
            o.Psexec = psexec;

            string deployError;
            string scriptPath = DeployScript(o, out deployError);
            if (scriptPath == null) { Warn(deployError); return; }
            o.Script = scriptPath;

            if (o.Delivery == "path" && !File.Exists(o.Script)) problems.Add("не найден скрипт .ps1: " + o.Script);
            if (string.IsNullOrEmpty(o.Psexec) || !File.Exists(o.Psexec)) problems.Add("не найден PsExec.exe");
        }

        if (string.IsNullOrEmpty(o.DistribDir)) problems.Add("не задан каталог дистрибутивов -dd");
        if (string.IsNullOrEmpty(o.LogDir)) problems.Add("не задан каталог журналов -dl");
        if (string.IsNullOrEmpty(o.InstallMode)) problems.Add("не выбран режим установки -ip");
        if (string.IsNullOrEmpty(o.DeleteMode)) problems.Add("не выбран режим удаления -dp");
        if (!string.IsNullOrEmpty(o.User) && o.Password.Length == 0)
            problems.Add("не введён пароль (psexec запросит его и зависнет)");
        if (!o.DesignerAllClients && !o.ThinClient && !o.ThinClientFile && !DryRun)
            problems.Add("не выбран ни один компонент -iod");

        if (problems.Count > 0)
        {
            Warn("Проверьте параметры:\n\n— " + string.Join("\n— ", problems.ToArray()));
            return;
        }

        Jobs.Clear();
        _jobs = new List<Job>();
        foreach (string pc in pcs)
        {
            var job = new Job { Name = pc };
            _jobs.Add(job);
            Jobs.Add(job);
        }

        UpdateKpi();

        if (o.DryRun)
        {
            AddLog(Palette.Info, "Режим «только показать команды» — ничего не выполняется.");
            foreach (Job job in _jobs)
            {
                AddLog(Palette.Text, "[" + job.Name + "] " + o.Psexec + " " + o.BuildArgs(job.Name, null));
                job.State = JobState.Ok;
                job.Note = "dry-run";
            }
            StatusText = "Команды выведены в журнал";
            ProgressText = _jobs.Count + " / " + _jobs.Count;
            ProgressMax = _jobs.Count;
            Progress = _jobs.Count;
            return;
        }

        IsRunning = true;
        _startedAll = DateTime.Now;
        ProgressMax = _jobs.Count;
        Progress = 0;
        ProgressText = "0 / " + _jobs.Count;
        CheckSummary = "";

        AddLog(Palette.Muted, new string('─', 100));
        AddLog(Palette.Info, "Запуск: " + _jobs.Count + " ПК, потоков " + o.Threads + ", " + DateTime.Now.ToString("HH:mm:ss"));
        AddLog(Palette.Muted, "Установка -ip " + o.InstallMode + " · удаление -dp " + o.DeleteMode + " · компоненты " + o.Iod);

        _runner = new Runner(o, _jobs);
        _runner.JobStarted += job => Post(() => UpdateKpi());
        _runner.JobChanged += job => Post(() => OnJobChanged(job));
        _runner.LogLine += (job, line, system) => Post(() => OnLogLine(job, line, system));
        _runner.Finished += () => Post(OnAllFinished);
        _runner.Start();
    }

    private void StopRun()
    {
        if (!IsRunning) return;
        AddLog(Palette.Warn, "Остановка по требованию пользователя…");
        if (_runner != null) _runner.Stop();
    }

    private void OnJobChanged(Job job)
    {
        int done = 0;
        foreach (Job j in _jobs)
        {
            if (j.State == JobState.Ok || j.State == JobState.Warning || j.State == JobState.Failed ||
                j.State == JobState.Canceled || j.State == JobState.Skipped) done++;
        }
        Progress = Math.Min(done, ProgressMax);
        ProgressText = done + " / " + _jobs.Count;
        UpdateKpi();
    }

    private void OnAllFinished()
    {
        IsRunning = false;

        int ok = 0, warn = 0, fail = 0, cancel = 0, skip = 0;
        foreach (Job j in _jobs)
        {
            if (j.State == JobState.Ok) ok++;
            else if (j.State == JobState.Warning) warn++;
            else if (j.State == JobState.Failed) fail++;
            else if (j.State == JobState.Canceled) cancel++;
            else if (j.State == JobState.Skipped) skip++;
        }

        double sec = (DateTime.Now - _startedAll).TotalSeconds;
        string summary = string.Format("Итого: {0} ПК · успешно {1} · уже стояло {2} · с замечаниями {3} · ошибок {4}{5} · {6:0.0} с",
            _jobs.Count, ok, skip, warn, fail, cancel > 0 ? " · отменено " + cancel : "", sec);

        AddLog(fail == 0 && warn == 0 ? Palette.Ok : Palette.Warn, summary);
        StatusText = summary;
        CheckSummary = summary;
        UpdateKpi();
    }

    private void UpdateKpi()
    {
        int total = _jobs.Count;
        int success = _jobs.Count(j => j.State == JobState.Ok || j.State == JobState.Skipped);
        int failed = _jobs.Count(j => j.State == JobState.Failed);
        int running = _jobs.Count(j => j.State == JobState.Running);

        KpiTotal.Value = total.ToString();
        KpiSuccess.Value = success.ToString();
        KpiFailed.Value = failed.ToString();
        KpiRunning.Value = running.ToString();
    }

    private void Tick()
    {
        if (!IsRunning) return;
        foreach (Job j in _jobs)
        {
            if (j.State == JobState.Running || j.State == JobState.Pending) j.RaiseAll();
        }
    }

    // ===================== Журнал =====================

    private static readonly Regex RxErr = new Regex(@"(?i)(\berror\b|ошибк|не удалось|не найден|не найдена|прервана|отказано|недопустим)", RegexOptions.Compiled);
    private static readonly Regex RxWarn = new Regex(@"(?i)(предупрежд|\bwarn|не будет|пропущен|уже установлен)", RegexOptions.Compiled);
    private static readonly Regex RxOk = new Regex(@"(?i)(окончание работы|успешно|установка 1с)", RegexOptions.Compiled);

    private void OnLogLine(Job job, string line, bool system)
    {
        var color = system ? Palette.Muted : Classify(line);
        AddLog(color, "[" + job.Name + "] " + line);
    }

    private static System.Windows.Media.Brush Classify(string line)
    {
        if (string.IsNullOrEmpty(line)) return Palette.Text;
        if (RxOk.IsMatch(line)) return Palette.Ok;
        if (RxErr.IsMatch(line)) return Palette.Err;
        if (RxWarn.IsMatch(line)) return Palette.Warn;
        return Palette.Text;
    }

    public void AddLog(System.Windows.Media.Brush color, string text)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(new Action(() => AddLog(color, text)));
            return;
        }

        LogEntries.Add(new LogEntry(text ?? "", color));
        if (LogEntries.Count > 5000) LogEntries.RemoveAt(0);
    }

    private void Post(Action action)
    {
        try { _dispatcher.BeginInvoke(action); } catch { }
    }

    // ===================== Отчёт =====================

    private void SaveReport()
    {
        if (_jobs.Count == 0) { Warn("Отчёт пуст."); return; }
        var dlg = new SaveFileDialog
        {
            Title = "Сохранить отчёт",
            Filter = "Текст (*.txt)|*.txt|CSV (*.csv)|*.csv|Все файлы (*.*)|*.*",
            FileName = "1c-update-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            if (dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("Компьютер;Статус;Длительность,с;Код;Примечание");
                foreach (Job j in _jobs)
                    sb.AppendLine(j.Name + ";" + j.StateText + ";" + j.Seconds.ToString("0.0") + ";" +
                                  (j.Code == -9999 ? "" : j.Code.ToString()) + ";" + j.Note.Replace(";", ","));
            }
            else
            {
                sb.AppendLine("Отчёт 1C Update " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                foreach (Job j in _jobs)
                    sb.AppendLine(string.Format("{0,-20} {1,-18} {2,8:0.0} с  код {3}  {4}",
                        j.Name, j.StateText, j.Seconds, j.Code, j.Note));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            StatusText = "Отчёт сохранён: " + dlg.FileName;
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }
    }

    // ===================== Настройки =====================

    private void LoadSettings()
    {
        if (Embedded.HasPsexec) UseEmbeddedPsexec = _settings.Get("psexecembedded", "1") != "0";
        PsexecPath = _settings.Get("psexec", VersionScanner.FindPsexec());
        ScriptPath = _settings.Get("script", "");
        DistribDir = _settings.Get("dd", "");
        LogDir = _settings.Get("dl", "");
        User = _settings.Get("user", "");

        int threads;
        if (int.TryParse(_settings.Get("threads", "4"), out threads) && threads >= 1 && threads <= 64) Threads = threads;

        string ip = _settings.Get("ip", "last");
        if (ip == "no") InstallNo = true;
        else if (ip == "last" || string.IsNullOrEmpty(ip)) InstallLast = true;
        else { InstallVersionSelected = true; InstallVersion = ip; }

        string dp = _settings.Get("dp", "no");
        if (dp == "no" || string.IsNullOrEmpty(dp)) DeleteNo = true;
        else if (dp == "ael") DeleteAel = true;
        else if (dp == "all") DeleteAll = true;
        else { DeleteVersionSelected = true; DeleteVersion = dp; }

        DesignerAllClients = _settings.Get("designer", "0") == "1";
        ThinClient = _settings.Get("thin", "1") != "0";
        ThinClientFile = _settings.Get("thinfiles", "0") == "1";

        string delivery = _settings.Get("delivery", "exe");
        DeliveryIndex = delivery == "share" ? 1 : delivery == "path" ? 2 : 0;

        Computers = _settings.Get("computers", "").Replace("\\n", Environment.NewLine);

        if (!string.IsNullOrEmpty(InstallVersion) && !AvailableVersions.Contains(InstallVersion))
            AvailableVersions.Add(InstallVersion);
        if (!string.IsNullOrEmpty(DeleteVersion) && !AvailableVersions.Contains(DeleteVersion))
            AvailableVersions.Add(DeleteVersion);
    }

    public void SaveSettings()
    {
        _settings.Set("psexec", PsexecPath.Trim());
        _settings.Set("script", ScriptPath.Trim());
        _settings.Set("dd", DistribDir.Trim());
        _settings.Set("dl", LogDir.Trim());
        _settings.Set("user", User.Trim());
        _settings.Set("threads", Threads.ToString());
        _settings.Set("ip", InstallMode);
        _settings.Set("dp", DeleteMode);
        _settings.Set("designer", DesignerAllClients ? "1" : "0");
        _settings.Set("thin", ThinClient ? "1" : "0");
        _settings.Set("thinfiles", ThinClientFile ? "1" : "0");
        _settings.Set("delivery", DeliveryMode);
        _settings.Set("psexecembedded", UseEmbeddedPsexec ? "1" : "0");
        _settings.Set("computers", (Computers ?? "").Replace("\r", "").Replace("\n", "\\n"));
        _settings.Save();
    }

    private static void Warn(string message)
    {
        MessageBox.Show(message, "1C Update", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
