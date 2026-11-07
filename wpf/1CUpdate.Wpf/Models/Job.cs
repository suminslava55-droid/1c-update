using System.Text;
using System.Windows.Media;
using OneCUpdate.Common;

namespace OneCUpdate.Models;

public enum JobState { Pending, Running, Ok, Warning, Failed, Canceled, Skipped }

/// <summary>Задача обновления одного ПК.</summary>
public sealed class Job : ObservableObject
{
    private readonly StringBuilder _text = new StringBuilder();
    private string _name = "";
    private JobState _state = JobState.Pending;
    private DateTime _started;
    private DateTime _finished;
    private int _code = -9999;
    private string _note = "";

    public string Name { get => _name; set => Set(ref _name, value); }

    public JobState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(StateBrush));
            OnPropertyChanged(nameof(RowBackground));
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(CodeText));
        }
    }

    public DateTime Started
    {
        get => _started;
        set { if (Set(ref _started, value)) OnPropertyChanged(nameof(DurationText)); }
    }

    public DateTime Finished
    {
        get => _finished;
        set { if (Set(ref _finished, value)) OnPropertyChanged(nameof(DurationText)); }
    }

    public int Code
    {
        get => _code;
        set { if (Set(ref _code, value)) OnPropertyChanged(nameof(CodeText)); }
    }

    public string Note { get => _note; set => Set(ref _note, value); }

    public DateTime LastOutput { get; set; } = DateTime.Now;
    public bool MarkerSeen { get; set; }
    public int Attempt { get; set; }

    public void AppendOutput(string line)
    {
        lock (_text)
        {
            _text.AppendLine(line);
            if (_text.Length > 400000) _text.Remove(0, 200000);
        }
    }

    public void ClearOutput()
    {
        lock (_text) _text.Clear();
    }

    public string GetOutput()
    {
        lock (_text) return _text.ToString();
    }

    public int OutputLength
    {
        get { lock (_text) return _text.Length; }
    }

    public double Seconds
    {
        get
        {
            DateTime end = State == JobState.Running || State == JobState.Pending ? DateTime.Now : Finished;
            return Math.Max(0, (end - Started).TotalSeconds);
        }
    }

    public string StateText
    {
        get
        {
            switch (State)
            {
                case JobState.Pending: return "Ожидание";
                case JobState.Running: return "Выполняется…";
                case JobState.Ok: return "Готово";
                case JobState.Warning: return "С замечаниями";
                case JobState.Failed: return "Ошибка";
                case JobState.Canceled: return "Отменено";
                case JobState.Skipped: return "Уже установлена";
            }
            return "";
        }
    }

    public string DurationText
    {
        get
        {
            if (State == JobState.Pending) return "";
            int s = (int)Math.Round(Seconds);
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }
    }

    public string CodeText => Code == -9999 ? "" : Code.ToString();

    public Brush StateBrush
    {
        get
        {
            switch (State)
            {
                case JobState.Ok: return Palette.Ok;
                case JobState.Warning: return Palette.Warn;
                case JobState.Failed: return Palette.Err;
                case JobState.Running: return Palette.Info;
                case JobState.Skipped: return Palette.Info;
                case JobState.Canceled: return Palette.Muted;
                default: return Palette.Text;
            }
        }
    }

    public Brush RowBackground
    {
        get
        {
            switch (State)
            {
                case JobState.Ok: return Palette.OkBg;
                case JobState.Warning: return Palette.WarnBg;
                case JobState.Failed: return Palette.ErrBg;
                case JobState.Running: return Palette.InfoBg;
                default: return Brushes.Transparent;
            }
        }
    }
}

/// <summary>Кисти состояний (совпадают с палитрой темы).</summary>
public static class Palette
{
    public static readonly Brush Text = Frozen("#E4E4E8");
    public static readonly Brush Muted = Frozen("#9696A0");
    public static readonly Brush Info = Frozen("#78B4F0");
    public static readonly Brush Ok = Frozen("#89D185");
    public static readonly Brush Warn = Frozen("#DBCD78");
    public static readonly Brush Err = Frozen("#F48771");

    public static readonly Brush InfoBg = Frozen("#14283C");
    public static readonly Brush OkBg = Frozen("#173C23");
    public static readonly Brush WarnBg = Frozen("#3C3214");
    public static readonly Brush ErrBg = Frozen("#3C1E19");

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
