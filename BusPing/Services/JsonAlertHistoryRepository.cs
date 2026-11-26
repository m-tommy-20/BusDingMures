using BusPing.Models;
using BusPing.Services.Interfaces;

namespace BusPing.Services;

/// <summary>JSON-file backed alert history, capped so it cannot grow forever.</summary>
public sealed class JsonAlertHistoryRepository : IAlertHistoryRepository
{
    private const int MaxEntries = 1000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = JsonStore.AppDataPath("alert-history.json");
    private List<AlertEvent>? _cache;

    public async Task<IReadOnlyList<AlertEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await LoadAsync(cancellationToken);
            return list.OrderByDescending(a => a.FiredAt).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(AlertEvent alertEvent, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await LoadAsync(cancellationToken);
            list.Add(alertEvent);
            if (list.Count > MaxEntries)
            {
                list.RemoveRange(0, list.Count - MaxEntries);
            }

            await JsonStore.WriteAsync(_path, list, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _cache = [];
            await JsonStore.WriteAsync(_path, _cache, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<AlertEvent>> LoadAsync(CancellationToken cancellationToken)
    {
        _cache ??= await JsonStore.ReadAsync<List<AlertEvent>>(_path, cancellationToken) ?? [];
        return _cache;
    }
}
