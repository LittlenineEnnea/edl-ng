using System.Collections.Specialized;
using QCEDL.GUI.Services;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class LogsViewModel : ViewModelBase
{
    private string _text = string.Empty;

    public LogsViewModel(ObservableLogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Sink = sink;
        Sink.Entries.CollectionChanged += OnEntriesChanged;
        UpdateText();
    }

    public ObservableLogSink Sink { get; }

    public string Text
    {
        get => _text;
        private set => this.RaiseAndSetIfChanged(ref _text, value);
    }

    public void Clear()
    {
        Sink.Clear();
        UpdateText();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateText();
    }

    private void UpdateText()
    {
        // Format each log entry as "Time Level Message" and join them with new lines
        Text = string.Join(Environment.NewLine, Sink.Entries.Select(entry => $"{entry.TimeText} {entry.LevelText} {entry.Message}"));
    }
}