using OneCUpdate.Common;

namespace OneCUpdate.Models;

/// <summary>KPI-карточка статистики.</summary>
public sealed class KpiItem : ObservableObject
{
    private string _value = "0";
    private string _label = "";
    private string _accent = "#78B4F0";

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    public string Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    public string Accent
    {
        get => _accent;
        set => Set(ref _accent, value);
    }
}
