using BusPing.Models;
using BusPing.Services.Interfaces;

namespace BusPing.Services;

/// <summary>JSON-file backed watch storage; the single source of user rules.</summary>
public sealed class JsonWatchRepository : IWatchRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = JsonStore.AppDataPath("watches.json");
    private List<WatchRule>? _cache;

    public async Task<IReadOnlyList<WatchRule>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadAsync(cancellationToken)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WatchRule?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadAsync(cancellationToken)).FirstOrDefault(w => w.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(WatchRule watch, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await LoadAsync(cancellationToken);
            var index = list.FindIndex(w => w.Id == watch.Id);
            if (index >= 0)
            {
                list[index] = watch;
            }
            else
            {
                list.Add(watch);
            }

            await SaveAsync(list, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await LoadAsync(cancellationToken);
            list.RemoveAll(w => w.Id == id);
            await SaveAsync(list, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<WatchRule>> LoadAsync(CancellationToken cancellationToken)
    {
        _cache ??= await JsonStore.ReadAsync<List<WatchRule>>(_path, cancellationToken) ?? [];
        return _cache;
    }

    private async Task SaveAsync(List<WatchRule> list, CancellationToken cancellationToken)
    {
        await JsonStore.WriteAsync(_path, list, cancellationToken);
    }
}
