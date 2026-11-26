using System.Text;

namespace BusPing.Services;

/// <summary>
/// Minimal RFC 4180 CSV reader for the GTFS feed. The feed is UTF-8 with quoted
/// fields that may contain commas, which rules out a naive Split(',').
/// </summary>
public static class Csv
{
    public static IEnumerable<string[]> ReadRows(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            yield return SplitLine(line);
        }
    }

    /// <summary>Reads the file and returns rows as header-keyed dictionaries.</summary>
    public static IEnumerable<Dictionary<string, string>> ReadRecords(string path)
    {
        string[]? header = null;
        foreach (var row in ReadRows(path))
        {
            if (header is null)
            {
                header = row;
                continue;
            }

            var record = new Dictionary<string, string>(header.Length, StringComparer.Ordinal);
            for (var i = 0; i < header.Length && i < row.Length; i++)
            {
                record[header[i]] = row[i];
            }

            yield return record;
        }
    }

    public static string[] SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else if (c != '\r')
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return [.. fields];
    }
}
