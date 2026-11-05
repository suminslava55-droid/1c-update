using System;
using System.Diagnostics;
using System.IO;
using System.Text;

// Имитация psexec -c для автотеста окна: находит --remote-worker и запускает наш exe
// с остальными аргументами, транслируя его вывод так, как это делает настоящий psexec.
internal static class FakePsExec
{
    private static int Main(string[] args)
    {
        string pc = args.Length > 0 ? args[0].TrimStart('\\') : "?";
        int idx = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--remote-worker") { idx = i; break; }
        }
        if (idx < 0)
        {
            Console.WriteLine("fake psexec: в аргументах нет --remote-worker");
            return 9;
        }

        // для автотеста повторной попытки после «файл занят»
        string flag = Path.Combine(Path.GetTempPath(), "fake-copy-fail.flag");
        if (!File.Exists(flag))
        {
            File.WriteAllText(flag, "1");
            Console.WriteLine("Error copying C:\\Users\\it\\Desktop\\1CUpdate.exe to remote system:");
            Console.WriteLine("Не удалось скопировать файл: файл используется другим процессом");
            return 6;
        }
        try { File.Delete(flag); } catch { }

        string exe = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "1CUpdate.exe"));
        if (!File.Exists(exe))
        {
            Console.WriteLine("fake psexec: не найден " + exe);
            return 9;
        }

        var sb = new StringBuilder();
        for (int i = idx; i < args.Length; i++) sb.Append('"').Append(args[i]).Append("\" ");

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("PsExec-эмуляция: " + pc);

        // для автотеста «psexec завис после итога»
        if (pc.StartsWith("HANG", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("1C Update: версия 8.3.22.2239 уже установлена — установка не требуется");
            Console.WriteLine("== итог: skip 8.3.22.2239 ==");
            System.Threading.Thread.Sleep(120000);
            return 0;
        }

        // для автотеста ветки «уже установлена» — без реального обращения к реестру
        if (pc.StartsWith("SKIP", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("1C Update: установлено сейчас: 8.3.22.2239");
            Console.WriteLine("1C Update: версия 8.3.22.2239 уже установлена — установка не требуется");
            Console.WriteLine("== итог: skip 8.3.22.2239 ==");
            Console.WriteLine("PsExec-эмуляция: " + pc + " завершено, код 0");
            return 0;
        }

        var psi = new ProcessStartInfo(exe, sb.ToString());
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;

        var p = new Process();
        p.StartInfo = psi;
        p.Start();
        try { p.StandardInput.Close(); } catch { }

        p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e)
        {
            if (e.Data != null) Console.WriteLine(e.Data);
        };
        p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
        {
            if (e.Data != null) Console.WriteLine(e.Data);
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();

        Console.WriteLine("PsExec-эмуляция: " + pc + " завершено, код " + p.ExitCode);
        return p.ExitCode;
    }
}