using BusPing.Models;

namespace BusPing.Services.Interfaces;

public interface IWatchRepository
{
    Task<IReadOnlyList<WatchRule>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<WatchRule?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpsertAsync(WatchRule watch, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IAlertHistoryRepository
{
    Task<IReadOnlyList<AlertEvent>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(AlertEvent alertEvent, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler? Changed;

    Task SaveAsync(AppSettings settings);

    void ApplyTheme();
}
