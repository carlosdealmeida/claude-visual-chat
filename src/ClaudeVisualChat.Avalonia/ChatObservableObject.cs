using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClaudeVisualChat;

/// <summary>
/// Base of the library's observable types: raises <see cref="PropertyChanged"/> when a value changes. Named so it never
/// clashes with the <c>ObservableObject</c> of CommunityToolkit.Mvvm.
/// </summary>
public abstract class ChatObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets the field and raises <see cref="PropertyChanged"/>; returns false when the value is the same.</summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
