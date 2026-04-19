using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using QCEDL.CLI.Helpers;

namespace QCEDL.GUI.Services;

/// <summary>
/// Persists GUI preferences (currently the chosen UI culture) as JSON under the user's AppData
/// directory. Failures are logged but non-fatal — the GUI falls back to defaults.
/// </summary>
public static class GuiSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "edl-ng",
        "gui-settings.json");

    public static GuiSettingsModel Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new GuiSettingsModel();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<GuiSettingsModel>(json) ?? new GuiSettingsModel();
        }
        catch (Exception ex)
        {
            Logging.Log($"Failed to load GUI settings: {ex.Message}", LogLevel.Warning);
            return new GuiSettingsModel();
        }
    }

    public static void Save(GuiSettingsModel model)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(model, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Logging.Log($"Failed to save GUI settings: {ex.Message}", LogLevel.Warning);
        }
    }

    public static CultureInfo ResolveStartupCulture(GuiSettingsModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.Culture))
        {
            try
            {
                var requested = CultureInfo.GetCultureInfo(model.Culture);
                if (Localizer.SupportedCultures.Any(c => c.Name == requested.Name))
                {
                    return requested;
                }
            }
            catch (CultureNotFoundException)
            {
                // Fall through to detection below.
            }
        }

        var system = CultureInfo.CurrentUICulture;
        var match = Localizer.SupportedCultures.FirstOrDefault(c => c.Name == system.Name)
                    ?? Localizer.SupportedCultures.FirstOrDefault(c => c.TwoLetterISOLanguageName == system.TwoLetterISOLanguageName);
        return match ?? Localizer.SupportedCultures[0];
    }
}

public sealed class GuiSettingsModel
{
    [JsonPropertyName("culture")]
    public string? Culture { get; set; }
}