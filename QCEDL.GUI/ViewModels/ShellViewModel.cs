using System.Collections.ObjectModel;
using QCEDL.GUI.Services;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private NavigationItem _selected;

    public ShellViewModel(EdlService edlService, ObservableLogSink logs)
    {
        ArgumentNullException.ThrowIfNull(edlService);
        ArgumentNullException.ThrowIfNull(logs);

        Logs = new LogsViewModel(logs);
        Connection = new ConnectionViewModel(edlService);
        Overview = new OverviewViewModel(edlService, Connection);
        Partitions = new PartitionsViewModel(edlService);
        Settings = new SettingsViewModel();

        NavigationItems =
        [
            new NavigationItem("Nav_Overview", Overview),
            new NavigationItem("Nav_Connection", Connection),
            new NavigationItem("Nav_Partitions", Partitions),
            new NavigationItem("Nav_Sectors", new PlaceholderViewModel("Placeholder_Sectors_Title", "Placeholder_Sectors_Description")),
            new NavigationItem("Nav_RawProgram", new PlaceholderViewModel("Placeholder_RawProgram_Title", "Placeholder_RawProgram_Description")),
            new NavigationItem("Nav_Advanced", new PlaceholderViewModel("Placeholder_Advanced_Title", "Placeholder_Advanced_Description")),
            new NavigationItem("Nav_Logs", Logs),
            new NavigationItem("Nav_Settings", Settings),
        ];
        _selected = NavigationItems[0];
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public NavigationItem Selected
    {
        get => _selected;
        set => this.RaiseAndSetIfChanged(ref _selected, value);
    }

    public OverviewViewModel Overview { get; }
    public ConnectionViewModel Connection { get; }
    public PartitionsViewModel Partitions { get; }
    public LogsViewModel Logs { get; }
    public SettingsViewModel Settings { get; }
}

public sealed class NavigationItem : ViewModelBase
{
    public NavigationItem(string titleKey, ViewModelBase content)
    {
        TitleKey = titleKey;
        Content = content;
        Localizer.Instance.CultureChanged += (_, _) => this.RaisePropertyChanged(nameof(Title));
    }

    public string TitleKey { get; }
    public ViewModelBase Content { get; }
    public string Title => Localizer.Instance[TitleKey];
}

public sealed class PlaceholderViewModel : ViewModelBase
{
    public PlaceholderViewModel(string titleKey, string descriptionKey)
    {
        TitleKey = titleKey;
        DescriptionKey = descriptionKey;
        Localizer.Instance.CultureChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(Description));
        };
    }

    public string TitleKey { get; }
    public string DescriptionKey { get; }
    public string Title => Localizer.Instance[TitleKey];
    public string Description => Localizer.Instance[DescriptionKey];
}