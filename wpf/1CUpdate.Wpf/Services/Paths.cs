using System.IO;
using System.Text;

namespace OneCUpdate.Services;

public static class TempArea
{
    private static string _dir;

    public static string Dir
    {
        get
        {
            if (_dir != null) return _dir;
            var candidates = new List<string>();
            try { candidates.Add(AppContext.BaseDirectory); } catch { }
            try { candidates.Add(Path.GetTempPath()); } catch { }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1CUpdate"));
            foreach (string dir in candidates)
            {
                try
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    Directory.CreateDirectory(dir);
                    string probe = Path.Combine(dir, ".1cupdate-probe-" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    _dir = dir;
                    return dir;
                }
                catch { }
            }
            _dir = Path.GetTempPath();
            return _dir;
        }
    }
}

public static class Paths
{
    public static string SettingsFile
    {
        get
        {
            string custom = Environment.GetEnvironmentVariable("ONECUPDATE_CONFIG");
            if (!string.IsNullOrEmpty(custom)) return custom;
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1CUpdate");
            try
            {
                Directory.CreateDirectory(appData);
                return Path.Combine(appData, "settings.ini");
            }
            catch
            {
                try { return Path.Combine(AppContext.BaseDirectory, "1CUpdate.settings.ini"); }
                catch { return null; }
            }
        }
    }
}
