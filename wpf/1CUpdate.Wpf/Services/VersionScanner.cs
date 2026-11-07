using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OneCUpdate.Services;

/// <summary>Сканирование каталога дистрибутивов и проверка доступности путей.</summary>
public static class VersionScanner
{
    public static List<string> ScanVersions(string dir)
    {
        var result = new List<string>();
        try
        {
            foreach (string path in Directory.GetDirectories(dir))
            {
                string name = Path.GetFileName(path);
                Match m = Regex.Match(name, @"^(\d+\.\d+\.\d+\.\d+)(-(32|64))?$");
                if (!m.Success) continue;
                string ver = m.Groups[1].Value;
                if (!result.Contains(ver)) result.Add(ver);
            }
        }
        catch { }
        result.Sort((a, b) => CompareVersions(b, a));
        return result;
    }

    public static int CompareVersions(string a, string b)
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

    public static bool CanWriteTo(string dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir)) return false;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".1cupdate-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    public static string FindPsexec()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] common =
        {
            Path.Combine(baseDir, "PsExec64.exe"),
            Path.Combine(baseDir, "PsExec.exe"),
            Path.Combine(baseDir, @"my\PsExec64.exe"),
            Path.Combine(baseDir, @"my\PsExec.exe"),
            @"C:\inst\PsExec64.exe", @"C:\inst\PsExec.exe", @"C:\PsTools\PsExec.exe", @"C:\Windows\System32\PsExec.exe"
        };
        foreach (string p in common)
        {
            try { if (File.Exists(p)) return p; } catch { }
        }
        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in pathVar.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                string p = Path.Combine(dir.Trim(), "PsExec.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return "";
    }

    public static Encoding DetectEncoding(string path)
    {
        try
        {
            byte[] b = new byte[3];
            using (var fs = File.OpenRead(path)) { fs.Read(b, 0, 3); }
            if (b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8;
            if (b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode;
            return Encoding.GetEncoding(1251);
        }
        catch { return Encoding.Default; }
    }
}
