using BusPing.Models;
using BusPing.Services.Interfaces;

namespace BusPing.Services;

/// <summary>
/// Settings backed by MAUI Preferences, so they survive restarts without a
/// database. Theme changes are applied immediately through the app resource.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string ThemeKey = "settings.theme";
    private const string PollSecondsKey = "settings.pollSeconds";
    private const string LeadMinutesKey = "settings.leadMinutes";
    private const string SoundKey = "settings.sound";
    private const string ConfirmDeleteKey = "settings.confirmDelete";
    private const string LanguageKey = "settings.language";

    public SettingsService()
    {
        Current = new AppSettings
        {
            Theme = ReadEnum(ThemeKey, AppThemeMode.System),
            PollSeconds = Clamp(Preferences.Get(PollSecondsKey, 5), 5, 60),
            DefaultLeadMinutes = Clamp(Preferences.Get(LeadMinutesKey, 5), 1, 30),
            SoundEnabled = Preferences.Get(SoundKey, true),
            ConfirmBeforeDelete = Preferences.Get(ConfirmDeleteKey, true),
            Language = Preferences.Get(LanguageKey, "en"),
        };
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    public Task SaveAsync(AppSettings settings)
    {
        settings.PollSeconds = Clamp(settings.PollSeconds, 5, 60);
        settings.DefaultLeadMinutes = Clamp(settings.DefaultLeadMinutes, 1, 30);

        Preferences.Set(ThemeKey, settings.Theme.ToString());
        Preferences.Set(PollSecondsKey, settings.PollSeconds);
        Preferences.Set(LeadMinutesKey, settings.DefaultLeadMinutes);
        Preferences.Set(SoundKey, settings.SoundEnabled);
        Preferences.Set(ConfirmDeleteKey, settings.ConfirmBeforeDelete);
        Preferences.Set(LanguageKey, settings.Language);

        Current = settings.Clone();
        ApplyTheme();
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void ApplyTheme()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.UserAppTheme = Current.Theme switch
        {
            AppThemeMode.Light => AppTheme.Light,
            AppThemeMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    }

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private static AppThemeMode ReadEnum(string key, AppThemeMode fallback) =>
        Enum.TryParse<AppThemeMode>(Preferences.Get(key, fallback.ToString()), out var parsed)
            ? parsed
            : fallback;
}
