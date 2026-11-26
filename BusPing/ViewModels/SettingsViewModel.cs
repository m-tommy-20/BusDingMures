using BusPing.Models;
using BusPing.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusPing.ViewModels;

public sealed partial class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settings;
    private readonly IGtfsService _gtfs;

    public SettingsViewModel(ISettingsService settings, IGtfsService gtfs)
    {
        _settings = settings;
        _gtfs = gtfs;

        var current = settings.Current;
        _themeIndex = (int)current.Theme;
        _pollSeconds = current.PollSeconds;
        _defaultLeadMinutes = current.DefaultLeadMinutes;
        _soundEnabled = current.SoundEnabled;
        _confirmBeforeDelete = current.ConfirmBeforeDelete;
    }

    public string Title => "Settings";

    public string[] ThemeOptions { get; } = ["Follow system", "Light", "Dark"];

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private int _pollSeconds;

    [ObservableProperty]
    private int _defaultLeadMinutes;

    [ObservableProperty]
    private bool _soundEnabled;

    [ObservableProperty]
    private bool _confirmBeforeDelete;

    [ObservableProperty]
    private string _scheduleStatus = string.Empty;

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            var updated = _settings.Current.Clone();
            updated.Theme = (AppThemeMode)ThemeIndex;
            updated.PollSeconds = PollSeconds;
            updated.DefaultLeadMinutes = DefaultLeadMinutes;
            updated.SoundEnabled = SoundEnabled;
            updated.ConfirmBeforeDelete = ConfirmBeforeDelete;

            await _settings.SaveAsync(updated);
            StatusMessage = "Settings saved";
            HasError = false;
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshScheduleAsync()
    {
        IsBusy = true;
        ScheduleStatus = "Downloading schedule…";
        try
        {
            var data = await _gtfs.LoadAsync(forceRefresh: true);
            ScheduleStatus = $"Updated: {data.Routes.Count} lines · {data.Stops.Count} stops";
            HasError = false;
        }
        catch (Exception ex)
        {
            ScheduleStatus = "Update failed";
            HasError = true;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ApplyTheme() => _settings.ApplyTheme();
}
