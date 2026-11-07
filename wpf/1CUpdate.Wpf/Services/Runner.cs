using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using OneCUpdate.Models;

namespace OneCUpdate.Services;

/// <summary>Очередь ПК, N потоков, psexec + хвост журнала.</summary>
public sealed class Runner
{
    private readonly Options _opt;
    private readonly List<Job> _jobs;
    private readonly ConcurrentQueue<Job> _queue = new ConcurrentQueue<Job>();
    private readonly List<Process> _procs = new List<Process>();
    private volatile bool _stop;
    private readonly object _dupLock = new object();
    private readonly Dictionary<string, DateTime> _recent = new Dictionary<string, DateTime>();

    public event Action<Job> JobStarted;
    public event Action<Job> JobChanged;
    public event Action<Job, string, bool> LogLine;
    public event Action Finished;

    public Runner(Options opt, List<Job> jobs)
    {
        _opt = opt;
        _jobs = jobs;
    }

    public void Start()
    {
        foreach (Job j in _jobs) _queue.Enqueue(j);
        int threads = Math.Max(1, Math.Min(_opt.Threads, Math.Max(1, _jobs.Count)));
        for (int i = 0; i < threads; i++)
        {
            var t = new Thread(Worker) { IsBackground = true };
            t.Start();
        }

        var mon = new Thread(Monitor) { IsBackground = true };
        mon.Start();
    }

    public void Stop()
    {
        _stop = true;
        lock (_procs)
        {
            foreach (Process p in _procs)
            {
                try { if (!p.HasExited) p.Kill(); } catch { }
            }
        }
    }

    private void Worker()
    {
        Job job;
        while (!_stop && _queue.TryDequeue(out job))
        {
            job.Started = DateTime.Now;
            job.State = JobState.Running;
            Raise(JobStarted, job);

            try { Execute(job); }
            catch (Exception ex)
            {
                job.State = JobState.Failed;
                job.Note = ex.Message;
            }

            if (job.State == JobState.Running)
            {
                job.State = JobState.Failed;
                if (string.IsNullOrEmpty(job.Note)) job.Note = "неизвестный результат";
            }
            job.Finished = DateTime.Now;
            Raise(JobChanged, job);
        }
    }

    private void Monitor()
    {
        while (true)
        {
            Thread.Sleep(200);
            bool anyRunning = false;
            bool anyPending = false;
            foreach (Job j in _jobs)
            {
                if (j.State == JobState.Running) anyRunning = true;
                if (j.State == JobState.Pending) anyPending = true;
            }
            if (!anyRunning && (!anyPending || _stop))
            {
                if (_stop)
                {
                    foreach (Job j in _jobs)
                    {
                        if (j.State == JobState.Pending)
                        {
                            j.State = JobState.Canceled;
                            j.Note = "не запускалось";
                            Raise(JobChanged, j);
                        }
                    }
                }
                var handler = Finished;
                if (handler != null) handler();
                return;
            }
        }
    }

    private void Raise(Action<Job> handler, Job job)
    {
        if (handler == null) return;
        try { handler(job); } catch { }
    }

    private void Send(Job job, string line, bool system)
    {
        if (line == null) return;
        if (line.Length > 4000) line = line.Substring(0, 4000) + "…";
        if (IsDuplicate(line)) return;
        job.LastOutput = DateTime.Now;
        if (line.StartsWith("== итог:", StringComparison.Ordinal)) job.MarkerSeen = true;
        job.AppendOutput(line);
        var h = LogLine;
        if (h != null) { try { h(job, line, system); } catch { } }
    }

    private void Execute(Job job)
    {
        job.Attempt++;
        LogTail tail = null;
        LogTail progress = null;
        string progressPath = null;

        if (!string.IsNullOrEmpty(_opt.LogDir))
        {
            if (_opt.Delivery == "exe")
            {
                string runId = Guid.NewGuid().ToString("N").Substring(0, 8);
                progressPath = Path.Combine(_opt.LogDir, job.Name + "." + runId + ".progress.txt");
                progress = new LogTail(progressPath);
                progress.Prepare();
            }
            else
            {
                string logName = job.Name;
                if (Regex.IsMatch(logName, @"^\d{1,3}(\.\d{1,3}){3}$"))
                {
                    try { logName = Dns.GetHostEntry(logName).HostName.Split('.')[0]; } catch { }
                }
                tail = new LogTail(Path.Combine(_opt.LogDir, logName + ".log"));
                tail.Prepare();
            }
        }

        string args = _opt.BuildArgs(job.Name, progressPath);
        Send(job, "psexec " + args, true);

        var psi = new ProcessStartInfo
        {
            FileName = _opt.Psexec,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Encoding outEnc = _opt.Delivery == "exe"
            ? Encoding.UTF8
            : Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        psi.StandardOutputEncoding = outEnc;
        psi.StandardErrorEncoding = outEnc;

        Process p;
        try
        {
            p = new Process { StartInfo = psi };
            p.Start();
        }
        catch (Exception ex)
        {
            job.State = JobState.Failed;
            job.Note = ex.Message;
            Send(job, "не удалось запустить psexec: " + ex.Message, true);
            return;
        }

        try { p.StandardInput.Close(); } catch { }
        lock (_procs) _procs.Add(p);

        var tOut = new Thread(() => Pump(p.StandardOutput, job)) { IsBackground = true };
        tOut.Start();
        var tErr = new Thread(() => Pump(p.StandardError, job)) { IsBackground = true };
        tErr.Start();

        DateTime started = job.Started;
        bool killedByStop = false;
        bool timedOut = false;
        bool forcedByMarker = false;

        while (true)
        {
            Flush(tail, job);
            Flush(progress, job);
            if (p.WaitForExit(600)) break;

            if (job.MarkerSeen && (DateTime.Now - job.LastOutput).TotalSeconds > 25)
            {
                Send(job, "1C Update: psexec не завершился, но итог получен — завершаю принудительно", true);
                forcedByMarker = true;
                try { p.Kill(); } catch { }
                break;
            }

            if (_stop)
            {
                try { p.Kill(); } catch { }
                killedByStop = true;
                break;
            }
            if ((DateTime.Now - started).TotalMinutes > _opt.TimeoutMinutes)
            {
                try { p.Kill(); } catch { }
                timedOut = true;
                break;
            }
        }

        try { p.WaitForExit(8000); } catch { }
        tOut.Join(2000);
        tErr.Join(2000);
        Flush(tail, job);
        Flush(progress, job);

        if (progress != null && job.OutputLength == 0 && !string.IsNullOrEmpty(progressPath))
        {
            var again = new LogTail(progressPath);
            foreach (string line in again.ReadLines()) Send(job, line, false);
        }

        int code = -9999;
        try { code = p.ExitCode; } catch { }
        job.Code = code;

        if (!killedByStop) CleanupRemoteCopy(job);
        lock (_procs) _procs.Remove(p);
        try { p.Dispose(); } catch { }

        if (killedByStop)
        {
            job.State = JobState.Canceled;
            job.Note = "остановлено";
            return;
        }
        if (timedOut)
        {
            job.State = JobState.Failed;
            job.Note = "таймаут " + _opt.TimeoutMinutes + " мин";
            return;
        }

        string text = job.GetOutput();
        bool completed = text.IndexOf("Окончание работы скрипта", StringComparison.OrdinalIgnoreCase) >= 0;

        if (job.Attempt == 1 && code != 0 &&
            text.IndexOf("rror copying", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Send(job, "1C Update: копия программы на ПК занята прошлым процессом — снимаю блокировку и повторяю", true);
            KillLeftovers(job.Name);
            Thread.Sleep(2000);
            job.ClearOutput();
            job.MarkerSeen = false;
            Execute(job);
            return;
        }

        Match marker = Regex.Match(text, @"==\s*итог:\s*(skip|ok|fail|unknown)\s*([^=]*?)\s*==");
        string kind = marker.Success ? marker.Groups[1].Value : "";
        string detail = marker.Success ? marker.Groups[2].Value.Trim() : "";

        bool codeOk = code == 0 || forcedByMarker;

        if (kind == "skip" && codeOk)
        {
            job.State = JobState.Skipped;
            job.Note = "версия уже установлена" + (detail.Length > 0 ? " " + detail : "");
        }
        else if (kind == "ok" && codeOk)
        {
            job.State = JobState.Ok;
            job.Note = detail.Length > 0 ? "установлено " + detail : "";
        }
        else if (kind == "fail" || !codeOk)
        {
            job.State = JobState.Failed;
            if (detail.Length > 0) job.Note = detail;
            else if (code != 0) job.Note = "код psexec " + code + (code == 5 ? " (нет доступа)" : code == 53 ? " (ПК недоступен)" : code == 1 ? " (ошибка/отказ)" : "");
            else job.Note = "установка не подтверждена";
        }
        else if (!completed)
        {
            job.State = JobState.Warning;
            job.Note = job.OutputLength == 0
                ? "нет вывода: psexec ничего не вернул и файл прогресса не прочитан"
                : "итог не подтверждён: в выводе нет строки «== итог: … ==»";
        }
        else if (HasProblems(text))
        {
            job.State = JobState.Warning;
            job.Note = "в журнале есть сообщения об ошибках";
        }
        else
        {
            job.State = JobState.Ok;
            job.Note = "";
        }

        if (progressPath != null && (job.State == JobState.Ok || job.State == JobState.Skipped))
        {
            try { File.Delete(progressPath); } catch { }
        }
    }

    private void KillLeftovers(string pc)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _opt.Psexec,
                Arguments = _opt.BuildArgsKill(pc),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var p = new Process { StartInfo = psi };
            p.Start();
            p.WaitForExit(30000);
            try { p.Dispose(); } catch { }
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string lpLocalName;
        public string lpRemoteName;
        public string lpComment;
        public string lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string password, string username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    private void CleanupRemoteCopy(Job job)
    {
        if (_opt.Delivery != "exe" || string.IsNullOrEmpty(_opt.LocalExe)) return;
        string pc = job.Name;

        string fileName = Path.GetFileName(_opt.LocalExe);
        if (string.IsNullOrEmpty(fileName)) return;

        string share = @"\\" + pc + @"\C$";
        string target = share + @"\Windows\" + fileName;
        bool connected = false;

        try
        {
            if (!string.IsNullOrEmpty(_opt.User))
            {
                var nr = new NETRESOURCE { dwType = 1, lpRemoteName = share };
                int rc = WNetAddConnection2(ref nr, _opt.Password, _opt.User, 0);
                connected = (rc == 0 || rc == 85 || rc == 1219);
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!File.Exists(target)) return;
                    File.Delete(target);
                    Send(job, "1C Update: копия программы удалена с ПК", true);
                    return;
                }
                catch { Thread.Sleep(1000); }
            }
            Send(job, "1C Update: копию программы удалить не удалось (нет доступа к C$ или файл занят)", true);
        }
        catch { }
        finally
        {
            if (connected)
            {
                try { WNetCancelConnection2(share, 0, true); } catch { }
            }
        }
    }

    private bool IsDuplicate(string line)
    {
        lock (_dupLock)
        {
            DateTime when;
            if (_recent.TryGetValue(line, out when) && (DateTime.Now - when).TotalSeconds < 5) return true;
            if (_recent.Count > 400) _recent.Clear();
            _recent[line] = DateTime.Now;
            return false;
        }
    }

    private static bool HasProblems(string text)
    {
        string[] bad =
        {
            "не удалось", "прервана", "не найдена", "не найден", "не установлена",
            "ошибка", "недопустимое", "не допустимое", "не будет произведена", "не будут производиться"
        };
        string lower = text.ToLowerInvariant();
        foreach (string b in bad)
            if (lower.IndexOf(b, StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    private void Flush(LogTail tail, Job job)
    {
        if (tail == null) return;
        foreach (string line in tail.ReadLines()) Send(job, line, false);
    }

    private void Pump(StreamReader reader, Job job)
    {
        try
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Trim().Length == 0) continue;
                Send(job, line, true);
            }
        }
        catch { }
    }
}
