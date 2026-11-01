using System.ComponentModel;

namespace TakeABreakGoose;

/// <summary>One message in the Settings list, editable in place.</summary>
public sealed class EditableMessage : INotifyPropertyChanged
{
    private string _text;

    public EditableMessage(string text) => _text = text;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
