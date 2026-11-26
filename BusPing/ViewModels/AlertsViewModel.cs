using System.Collections.ObjectModel;
using BusPing.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusPing.ViewModels;

public sealed partial class AlertsViewModel : BaseViewModel
{
    private readonly IAlertHistoryRepository _repository;
    private readonly ISettingsService _settings;

    public AlertsViewModel(IAlertHistoryRepository repository, ISettingsService settings)
    {
        _repository = repository;
        _settings = settings;
        Title = "Alerts";
    }

    public string Title { get; }

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _averageEtaText = "—";

    [ObservableProperty]
    private bool _isEmpty = true;

    public ObservableCollection<AlertItemViewModel> Alerts { get; } = [];

    /// <summary>Alerts per day, newest first (LINQ GroupBy + Count).</summary>
    public ObservableCollection<string> DailySummary { get; } = [];

    public async Task InitializeAsync() => await LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var events = await _repository.GetAllAsync();

            Alerts.Clear();
            foreach (var alertEvent in events)
            {
                Alerts.Add(new AlertItemViewModel(alertEvent));
            }

            TotalCount = events.Count;
            AverageEtaText = events.Count == 0
                ? "—"
                : $"{events.Average(e => e.EtaMinutes):0.0} min";

            DailySummary.Clear();
            foreach (var group in events
                         .GroupBy(e => e.FiredAt.ToLocalTime().Date)
                         .OrderByDescending(g => g.Key)
                         .Take(14))
            {
                DailySummary.Add($"{group.Key:MMM d} · {group.Count()} alert(s)");
            }

            IsEmpty = events.Count == 0;
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
    private async Task ClearAsync()
    {
        if (_settings.Current.ConfirmBeforeDelete && Shell.Current is { } shell)
        {
            var confirmed = await shell.DisplayAlertAsync(
                "Clear history?",
                "Delete every stored alert event?",
                "Clear",
                "Cancel");

            if (!confirmed)
            {
                return;
            }
        }

        await _repository.ClearAsync();
        await LoadAsync();
    }
}
