using System.IO;
using System.Text;

namespace OneCUpdate.Services;

/// <summary>Диагностика: пишем необработанные исключения в файл</summary>
public static class CrashLog
{
    public static void Write(string text)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + Environment.MachineName + "] " + text + Environment.NewLine;
        var dirs = new List<string>();
        try { dirs.Add(Path.GetTempPath()); } catch { }
        try { dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")); } catch { }
        foreach (string dir in dirs)
        {
            try
            {
                File.AppendAllText(Path.Combine(dir, "1CUpdate-crash.log"), line, new UTF8Encoding(false));
                return;
            }
            catch { }
        }
    }
}
