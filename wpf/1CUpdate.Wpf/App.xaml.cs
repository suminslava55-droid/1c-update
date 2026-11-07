using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using OneCUpdate.Services;

namespace OneCUpdate;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Режим, который выполняется НА целевом ПК (psexec -c) — без окна.
        if (e.Args.Length >= 2 && e.Args[0] == "--remote-worker")
        {
            Shutdown(RemoteWorker.Run(e.Args));
            return;
        }

        // Пакетный режим: напечатать готовые команды для списка ПК.
        if (e.Args.Length >= 2 && e.Args[0] == "--commands")
        {
            int code = 0;
            try
            {
                var opt = new Models.Options
                {
                    Psexec = @"C:\inst\PsExec.exe",
                    Script = @"\\server\share\1C8InstallAndUninstall.ps1",
                    DistribDir = @"\\server\share\distr",
                    LogDir = @"\\server\share\log",
                    LocalExe = Environment.ProcessPath
                };
                var w = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                foreach (string raw in File.ReadAllLines(e.Args[1]))
                {
                    string pc = raw.Trim();
                    if (pc.Length == 0) continue;
                    w.WriteLine(opt.Psexec + " " + opt.BuildArgs(pc, null));
                }
            }
            catch (Exception ex)
            {
                code = 2;
                try { Console.Error.WriteLine(ex.Message); } catch { }
            }
            Shutdown(code);
            return;
        }

        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try { CrashLog.Write("Unhandled: " + args.ExceptionObject); } catch { }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " Dispatcher:" +
                              Environment.NewLine + args.Exception + Environment.NewLine;
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "1CUpdate-error.txt"), text);
            }
            catch { }
            try { CrashLog.Write("Dispatcher: " + args.Exception); } catch { }
            MessageBox.Show(args.Exception.Message, "1C Update", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
