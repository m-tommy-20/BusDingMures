using System.Collections.ObjectModel;
using BusPing.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusPing.ViewModels;

public sealed partial class WatchesViewModel : BaseViewModel
{
    private readonly IWatchRepository _repository;
    private readonly ISettingsService _settings;

    public WatchesViewModel(IWatchRepository repository, ISettingsService settings)
    {
        _repository = repository;
        _settings = settings;
        Title = "Watches";
    }

    public string Title { get; }

    [ObservableProperty]
    private bool _isEmpty = true;

    public ObservableCollection<WatchCardViewModel> Watches { get; } = [];

    public async Task InitializeAsync() => await LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var rules = await _repository.GetAllAsync();
            Watches.Clear();
            foreach (var rule in rules.OrderBy(r => r.RouteShortName).ThenBy(r => r.WindowStartMinutes))
            {
                Watches.Add(new WatchCardViewModel(rule));
            }

            IsEmpty = Watches.Count == 0;
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
    private async Task ToggleAsync(WatchCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        card.Watch.Enabled = !card.Watch.Enabled;
        await _repository.UpsertAsync(card.Watch);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(WatchCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        if (_settings.Current.ConfirmBeforeDelete && Shell.Current is { } shell)
        {
            var confirmed = await shell.DisplayAlertAsync(
                "Delete watch?",
                $"Remove \"{card.Name}\"? This cannot be undone.",
                "Delete",
                "Cancel");

            if (!confirmed)
            {
                return;
            }
        }

        await _repository.DeleteAsync(card.Watch.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private static Task AddAsync() => Shell.Current.GoToAsync(nameof(Views.WatchEditorPage));

    [RelayCommand]
    private static Task EditAsync(WatchCardViewModel? card) =>
        card is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{nameof(Views.WatchEditorPage)}?watchId={card.Watch.Id}");
}
