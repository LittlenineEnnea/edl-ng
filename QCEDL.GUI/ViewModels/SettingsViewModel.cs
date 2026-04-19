using System.Globalization;
using QCEDL.GUI.Services;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private CultureInfo _selectedLanguage;

    public SettingsViewModel()
    {
        _selectedLanguage = Localizer.Instance.Culture;

        // Keep selection synced if culture is changed elsewhere (e.g. from startup defaults).
        Localizer.Instance.CultureChanged += (_, _) =>
        {
            if (!Equals(_selectedLanguage, Localizer.Instance.Culture))
            {
                _selectedLanguage = Localizer.Instance.Culture;
                this.RaisePropertyChanged(nameof(SelectedLanguage));
            }
        };
    }

    public IReadOnlyList<CultureInfo> Languages { get; } = Localizer.SupportedCultures;

    public CultureInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || Equals(_selectedLanguage, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
            Localizer.Instance.Culture = value;
            GuiSettings.Save(new GuiSettingsModel { Culture = value.Name });
        }
    }
}