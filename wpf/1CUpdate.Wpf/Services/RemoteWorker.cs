using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OneCUpdate.Services;

/// <summary>Режим --remote-worker: выполняется НА целевом ПК (psexec -c).</summary>
public static class RemoteWorker
{
    public static int Run(string[] args)
    {
        try { return RunCore(args); }
        catch (Exception ex)
        {
            try
            {
                var w = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                w.WriteLine("1C Update: КРИТИЧЕСКАЯ ОШИБКА: " + ex.Message);
                w.WriteLine(ex.ToString());
                w.Flush();
            }
            catch { }
            CrashLog.Write("worker: " + ex);
            return 4;
        }
    }

    private static int RunCore(string[] args)
    {
        CrashLog.Write("старт worker: " + string.Join(" ", args));
        string dd = "", dl = "", ip = "no", dp = "no", iod = "", progressFile = "";
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "-dd" && i + 1 < args.Length) { dd = args[++i]; continue; }
            if (a == "-dl" && i + 1 < args.Length) { dl = args[++i]; continue; }
            if (a == "-ip" && i + 1 < args.Length) { ip = args[++i]; continue; }
            if (a == "-dp" && i + 1 < args.Length) { dp = args[++i]; continue; }
            if (a == "-iod" && i + 1 < args.Length) { iod = args[++i]; continue; }
            if (a == "--progress" && i + 1 < args.Length) { progressFile = args[++i]; continue; }
        }

        StreamWriter outW = null;
        try
        {
            outW = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch { }

        Action<string> W = s =>
        {
            try { if (_progress != null) _progress.WriteLine(s); } catch { }
            try { if (outW != null) { outW.WriteLine(s); outW.Flush(); } } catch { }
            try { CrashLog.Write(s); } catch { }
        };

        OpenProgress(dl, progressFile, W);

        byte[] data = Embedded.ScriptBytes;
        if (data == null) { W("1C Update: встроенный скрипт не найден"); return 3; }

        string temp = null;
        Exception lastError = null;
        foreach (string dir in new[] { Path.GetTempPath(), TempArea.Dir })
        {
            try
            {
                string candidate = Path.Combine(dir, "1c8-" + Guid.NewGuid().ToString("N") + ".ps1");
                File.WriteAllBytes(candidate, data);
                temp = candidate;
                break;
            }
            catch (Exception ex) { lastError = ex; }
        }
        if (temp == null)
        {
            W("1C Update: не удалось распаковать скрипт: " + (lastError != null ? lastError.Message : "нет доступа к каталогу"));
            return 3;
        }

        W("1C Update " + Embedded.ScriptVersion + " · " + Environment.MachineName + " · скрипт распакован");
        W("1C Update: -dd " + dd);
        W("1C Update: -dl " + dl);

        bool logOk = EnsureLogDir(dl, W);
        W(logOk ? "1C Update: каталог логов доступен" : "1C Update: каталог логов недоступен");

        string target = null;
        try { target = ResolveTargetVersion(ip, dd, W); }
        catch (Exception ex) { W("1C Update: ошибка определения версии: " + ex.Message); }
        W("1C Update: целевая версия: " + (target == null ? "не определена" : target));

        if (target != null)
        {
            List<string> before = null;
            try { before = InstalledVersions(); }
            catch (Exception ex) { W("1C Update: ошибка чтения реестра: " + ex.Message); }

            if (before == null)
            {
                W("1C Update: проверка установленной версии недоступна — продолжаю установку");
            }
            else
            {
                W("1C Update: установлено сейчас: " + (before.Count == 0 ? "платформ 1С не найдено" : string.Join(", ", before.ToArray())));
                if (before.Contains(target))
                {
                    W("1C Update: версия " + target + " уже установлена — установка не требуется");
                    W("== итог: skip " + target + " ==");
                    try { File.Delete(temp); } catch { }
                    CloseProgress();
                    return 0;
                }
                W("1C Update: установка требуется");
            }
        }

        LogTail tail = null;
        if (!string.IsNullOrEmpty(dl))
        {
            tail = new LogTail(Path.Combine(dl, Environment.MachineName + ".log"));
            tail.Prepare();
        }

        string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(ps)) ps = "powershell.exe";

        string psArgs = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + temp + "\"" +
                        " -dd \"" + dd + "\" -dl \"" + dl + "\" -ip \"" + ip + "\"" +
                        " -dp \"" + dp + "\" -iod \"" + iod + "\"";

        var psi = new ProcessStartInfo
        {
            FileName = ps,
            Arguments = psArgs,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };

        Process p;
        try
        {
            p = new Process { StartInfo = psi };
            p.Start();
        }
        catch (Exception ex)
        {
            W("1C Update: не удалось запустить PowerShell: " + ex.Message);
            return 3;
        }
        try { p.StandardInput.Close(); } catch { }

        var tOut = new Thread(() =>
        {
            try { string line; while ((line = p.StandardOutput.ReadLine()) != null) W(line); } catch { }
        }) { IsBackground = true };
        tOut.Start();
        var tErr = new Thread(() =>
        {
            try { string line; while ((line = p.StandardError.ReadLine()) != null) W(line); } catch { }
        }) { IsBackground = true };
        tErr.Start();

        while (!p.HasExited)
        {
            FlushLog(tail, W);
            Thread.Sleep(400);
        }
        try { p.WaitForExit(30000); } catch { }
        tOut.Join(2000);
        tErr.Join(2000);
        FlushLog(tail, W);

        int code = 0;
        try { code = p.ExitCode; } catch { }

        if (target != null)
        {
            List<string> after = InstalledVersions();
            if (after.Contains(target))
            {
                W("1C Update: проверка после установки — версия " + target + " найдена");
                W("== итог: ok " + target + " ==");
            }
            else
            {
                W("1C Update: проверка после установки — версия " + target + " НЕ найдена");
                W("== итог: fail версия " + target + " не найдена после установки ==");
            }
        }
        else
        {
            W("== итог: " + (code == 0 ? "ok" : "fail") + " ==");
        }

        try { File.Delete(temp); } catch { }
        CloseProgress();
        return code;
    }

    private static void FlushLog(LogTail tail, Action<string> write)
    {
        if (tail == null) return;
        foreach (string line in tail.ReadLines()) write(line);
    }

    private static StreamWriter _progress;

    private static void OpenProgress(string dl, string explicitPath, Action<string> write)
    {
        string path = !string.IsNullOrEmpty(explicitPath)
            ? explicitPath
            : (string.IsNullOrEmpty(dl) ? null : Path.Combine(dl, Environment.MachineName + ".progress.txt"));
        if (string.IsNullOrEmpty(path)) return;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (attempt > 0 && !string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _progress = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
                return;
            }
            catch (Exception ex)
            {
                if (attempt == 0) continue;
                write("1C Update: прогресс-файл недоступен (" + ex.Message + ") — остаётся только вывод psexec");
            }
        }
    }

    private static void CloseProgress()
    {
        try { if (_progress != null) { _progress.Flush(); _progress.Dispose(); } } catch { }
        _progress = null;
    }

    private static bool EnsureLogDir(string dl, Action<string> write)
    {
        if (string.IsNullOrEmpty(dl)) return false;
        try
        {
            if (!Directory.Exists(dl)) Directory.CreateDirectory(dl);
            return Directory.Exists(dl);
        }
        catch (Exception ex)
        {
            write("1C Update: каталог логов " + dl + " недоступен (" + ex.Message + ")");
            return false;
        }
    }

    private static List<string> InstalledVersions()
    {
        var list = new List<string>();
        string[] roots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };
        foreach (string root in roots)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(root))
                {
                    if (key == null) continue;
                    foreach (string sub in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey k = key.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                object name = k.GetValue("DisplayName");
                                if (name == null) continue;
                                if (!Regex.IsMatch(Convert.ToString(name), "^(1С|1C)")) continue;
                                object ver = k.GetValue("DisplayVersion");
                                if (ver == null) continue;
                                string v = Convert.ToString(ver).Trim();
                                if (v.Length > 0 && !list.Contains(v)) list.Add(v);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
        return list;
    }

    private static string ResolveTargetVersion(string ip, string dd, Action<string> write)
    {
        if (string.IsNullOrEmpty(ip) || ip == "no") return null;
        if (Regex.IsMatch(ip, @"^\d+\.\d+\.\d+\.\d+$")) return ip;
        if (ip != "last") return null;

        try
        {
            string best = null;
            foreach (string dir in Directory.GetDirectories(dd))
            {
                Match m = Regex.Match(Path.GetFileName(dir), @"^(\d+\.\d+\.\d+\.\d+)(-(32|64))?$");
                if (!m.Success) continue;
                string v = m.Groups[1].Value;
                if (best == null || CompareVersions(v, best) > 0) best = v;
            }
            if (best == null) write("1C Update: в " + dd + " нет папок с версиями — какую ставить, определит скрипт");
            return best;
        }
        catch (Exception ex)
        {
            write("1C Update: не удалось прочитать каталог дистрибутивов " + dd + " (" + ex.Message + ")");
            return null;
        }
    }

    private static int CompareVersions(string a, string b)
    {
        try
        {
            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
            {
                int x = Convert.ToInt32(pa[i]);
                int y = Convert.ToInt32(pb[i]);
                if (x != y) return x.CompareTo(y);
            }
            return pa.Length.CompareTo(pb.Length);
        }
        catch { return string.Compare(a, b, StringComparison.Ordinal); }
    }
}
