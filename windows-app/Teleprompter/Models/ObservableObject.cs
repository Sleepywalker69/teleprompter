using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Teleprompter.Models;

/// <summary>
/// Minimal INotifyPropertyChanged base so the shared <see cref="AppState"/> can drive
/// both windows via data binding without pulling in a full MVVM framework.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
