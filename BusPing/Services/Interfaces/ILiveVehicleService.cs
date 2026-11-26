using BusPing.Models;

namespace BusPing.Services.Interfaces;

public interface ILiveVehicleService
{
    DateTimeOffset? LastUpdated { get; }

    string? LastError { get; }

    /// <summary>True when the response comes from the on-disk last-known-good cache.</summary>
    bool IsStale { get; }

    /// <summary>Downloads and parses the current vehicle positions.</summary>
    Task<IReadOnlyList<VehiclePosition>> GetVehiclesAsync(CancellationToken cancellationToken = default);
}
