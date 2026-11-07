using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OneCUpdate.Services;

/// <summary>Чтение журнала с сетевой шары (PowerShell 5.1 пишет UTF-16).</summary>
public sealed class LogTail
{
    private readonly string _path;
    private long _pos;
    private Encoding _enc;
    private Decoder _dec;
    private readonly StringBuilder _pending = new StringBuilder();

    public LogTail(string path) { _path = path; }

    public void Prepare()
    {
        try
        {
            if (File.Exists(_path)) _pos = new FileInfo(_path).Length;
        }
        catch { }
    }

    public List<string> ReadLines()
    {
        var result = new List<string>();
        try
        {
            if (!File.Exists(_path)) return result;
            using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (_pos > fs.Length)
                {
                    _pos = 0;
                    _dec = _enc == null ? null : _enc.GetDecoder();
                    _pending.Length = 0;
                }

                if (_enc == null)
                {
                    if (fs.Length < 2) return result;
                    var bom = new byte[4];
                    int n = fs.Read(bom, 0, 4);
                    int bomLen = 0;
                    if (n >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) { _enc = new UnicodeEncoding(false, true); bomLen = 2; }
                    else if (n >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) { _enc = new UnicodeEncoding(true, true); bomLen = 2; }
                    else if (n >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) { _enc = new UTF8Encoding(false); bomLen = 3; }
                    else { _enc = new UTF8Encoding(false); }
                    _dec = _enc.GetDecoder();
                    if (_pos < bomLen) _pos = bomLen;
                }
                if (_dec == null) _dec = _enc.GetDecoder();
                if (fs.Length <= _pos) return result;
                fs.Seek(_pos, SeekOrigin.Begin);
                long avail = fs.Length - _pos;
                if (avail > 512 * 1024)
                {
                    _pos = fs.Length - 512 * 1024;
                    if (_enc is UnicodeEncoding && (_pos % 2) != 0) _pos++;
                    fs.Seek(_pos, SeekOrigin.Begin);
                    avail = fs.Length - _pos;
                }

                int size = (int)Math.Min(avail, 512 * 1024);
                if (_enc is UnicodeEncoding && (size % 2) != 0) size--;
                if (size <= 0) return result;

                var buf = new byte[size];
                int read = fs.Read(buf, 0, size);
                if (read <= 0) return result;
                _pos += read;

                int maxChars = _enc.GetMaxCharCount(read);
                var chars = new char[maxChars];
                int count = _dec.GetChars(buf, 0, read, chars, 0);
                _pending.Append(chars, 0, count);
            }

            string all = _pending.ToString();
            int start = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == '\n')
                {
                    string line = all.Substring(start, i - start).TrimEnd('\r');
                    start = i + 1;
                    if (line.Length > 0) result.Add(line);
                }
            }
            _pending.Remove(0, start);
            if (_pending.Length > 200000) _pending.Remove(0, _pending.Length - 100000);
        }
        catch { }
        return result;
    }
}
