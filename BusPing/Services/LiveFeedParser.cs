using System.Text.Json;
using BusPing.Models;

namespace BusPing.Services;

/// <summary>Parses live.transportlocal.ro/journeys.json without MAUI dependencies.</summary>
public static class LiveFeedParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<VehiclePosition> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
        }
        else if (!root.TryGetProperty("journeys", out array) &&
                 !root.TryGetProperty("vehicles", out array))
        {
            return [];
        }

        var result = new List<VehiclePosition>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            try
            {
                var vehicle = item.Deserialize<VehiclePosition>(Options);
                if (vehicle is not null && vehicle.VehicleId.Length > 0)
                {
                    result.Add(vehicle);
                }
            }
            catch (JsonException)
            {
                // One malformed record must not drop the whole feed.
            }
        }

        return result;
    }
}
