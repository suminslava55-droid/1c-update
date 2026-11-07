using System.Windows.Media;

namespace OneCUpdate.Models;

/// <summary>Строка журнала.</summary>
public sealed class LogEntry
{
    public LogEntry(string text, Brush color)
    {
        Text = text;
        Color = color;
    }

    public string Text { get; }
    public Brush Color { get; }
}
