using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace OneCUpdate.Services;

/// <summary>Р’СЃС‚СЂРѕРµРЅРЅС‹Рµ СЂРµСЃСѓСЂСЃС‹ (СЃРєСЂРёРїС‚ Рё PsExec)</summary>
public static class Embedded
{
    private static byte[] _script;
    private static bool _scriptRead;
    private static byte[] _psexec;
    private static bool _psexecRead;
    private static string _psexecName;
    private static byte[] _worker;
    private static bool _workerRead;

    public static bool HasScript => ScriptBytes != null;
    public static bool HasPsexec => PsexecBytes != null;

    /// <summary>Standalone-воркер (WinForms exe), который psexec копирует на целевой ПК.</summary>
    public static bool HasWorker => WorkerBytes != null;

    public static byte[] WorkerBytes
    {
        get
        {
            if (!_workerRead)
            {
                _worker = Read("worker.exe");
                _workerRead = true;
            }
            return _worker;
        }
    }

    public static byte[] ScriptBytes
    {
        get
        {
            if (!_scriptRead)
            {
                _script = Read("script.ps1");
                _scriptRead = true;
            }
            return _script;
        }
    }

    public static byte[] PsexecBytes
    {
        get
        {
            if (!_psexecRead)
            {
                if (Environment.Is64BitOperatingSystem)
                {
                    _psexec = Read("psexec64.exe");
                    if (_psexec != null) _psexecName = "PsExec64.exe";
                }
                if (_psexec == null)
                {
                    _psexec = Read("psexec.exe");
                    if (_psexec != null) _psexecName = "PsExec.exe";
                }
                _psexecRead = true;
            }
            return _psexec;
        }
    }

    public static string PsexecName
    {
        get
        {
            byte[] unused = PsexecBytes;
            return string.IsNullOrEmpty(_psexecName) ? "PsExec.exe" : _psexecName;
        }
    }

    public static string ScriptVersion
    {
        get
        {
            byte[] data = ScriptBytes;
            if (data == null) return "";
            try
            {
                string text = Encoding.UTF8.GetString(data);
                Match m = Regex.Match(text, @"\$ScriptVersion\s*=\s*""([\d\.]+)""");
                return m.Success ? m.Groups[1].Value : "";
            }
            catch { return ""; }
        }
    }

    private static byte[] Read(string name)
    {
        try
        {
            using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (s == null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    public static bool SameContent(string path, byte[] data)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length != data.Length) return false;
            using var fs = File.OpenRead(path);
            var buf = new byte[8192];
            int offset = 0;
            while (true)
            {
                int read = fs.Read(buf, 0, buf.Length);
                if (read <= 0) break;
                for (int i = 0; i < read; i++)
                    if (buf[i] != data[offset + i]) return false;
                offset += read;
            }
            return true;
        }
        catch { return false; }
    }
}

