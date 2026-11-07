using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OneCUpdate.Common;

/// <summary>Базовый класс для моделей и view-моделей.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        if (PropertyChanged != null)
            PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void RaiseAll()
    {
        if (PropertyChanged != null)
            PropertyChanged(this, new PropertyChangedEventArgs(string.Empty));
    }
}
