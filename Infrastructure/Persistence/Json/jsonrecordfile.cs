using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Persistence.Json;

/// <summary>Append-only JSON Lines file with one record per line and a trailing newline.</summary>
internal sealed class JsonRecordFile(string path)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Path { get; } = path;

    public async Task AppendAsync<T>(T record, CancellationToken ct)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        var line = JsonSerializer.Serialize(record, Options) + Environment.NewLine;
        await File.AppendAllTextAsync(Path, line, ct);
    }

    /// <summary>Reads every well-formed record; a torn final line is skipped rather than failing the read.</summary>
    public async Task<List<T>> ReadAllAsync<T>(CancellationToken ct)
    {
        var results = new List<T>();

        if (!File.Exists(Path))
        {
            return results;
        }

        using var reader = new StreamReader(File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<T>(line, Options);
                if (record is not null)
                {
                    results.Add(record);
                }
            }
            catch (JsonException)
            {
                // Ignore an incomplete trailing write; the store is append-only so earlier records stay valid.
            }
        }

        return results;
    }
}
