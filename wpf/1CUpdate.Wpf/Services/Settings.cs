using System.IO;
using System.Text;

namespace OneCUpdate.Services;

/// <summary>Настройки в %APPDATA%\1CUpdate\settings.ini (совместимы с WinForms-версией).</summary>
public sealed class Settings
{
    private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

    public string Get(string key, string fallback = "")
    {
        string value;
        return _values.TryGetValue(key, out value) ? value : fallback;
    }

    public void Set(string key, string value)
    {
        _values[key] = value ?? "";
    }

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            string path = Paths.SettingsFile;
            if (path == null || !File.Exists(path)) return s;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                s._values[raw.Substring(0, eq)] = raw.Substring(eq + 1);
            }
        }
        catch { }
        return s;
    }

    public void Save()
    {
        try
        {
            string path = Paths.SettingsFile;
            if (path == null) return;
            var sb = new StringBuilder();
            foreach (var pair in _values) sb.AppendLine(pair.Key + "=" + pair.Value);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }
}
