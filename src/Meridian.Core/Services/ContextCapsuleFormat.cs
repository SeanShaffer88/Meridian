using System.Text.Json;

namespace Meridian.Services;

/// <summary>Versioned, bounded JSON serialization for read-only Context Capsules. No filesystem I/O.</summary>
public static class ContextCapsuleFormat
{
    public const int Version = 1;
    public const int MaximumCapsules = 100;
    public const int MaximumDocumentCharacters = 1_000_000;

    private sealed record Document(int Version, Entry[] Capsules);
    private sealed record Entry(string Name, string[] Paths);

    public static string Serialize(IEnumerable<ContextCapsule> capsules)
    {
        ArgumentNullException.ThrowIfNull(capsules);
        var items = new List<Entry>();
        foreach (var capsule in capsules)
        {
            ArgumentNullException.ThrowIfNull(capsule);
            if (items.Count >= MaximumCapsules)
                throw new ArgumentException("Too many Capsules.", nameof(capsules));
            items.Add(new Entry(capsule.Name, capsule.Paths.ToArray()));
        }
        var result = JsonSerializer.Serialize(new Document(Version, items.ToArray()));
        if (result.Length > MaximumDocumentCharacters)
            throw new InvalidDataException("Capsule document exceeds the allowed size.");
        return result;
    }

    public static IReadOnlyList<ContextCapsule> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json.Length > MaximumDocumentCharacters)
            throw new InvalidDataException("Capsule document exceeds the allowed size.");

        Document? document;
        try { document = JsonSerializer.Deserialize<Document>(json); }
        catch (JsonException ex) { throw new InvalidDataException("Malformed Capsule JSON.", ex); }

        if (document is null || document.Version != Version || document.Capsules is null ||
            document.Capsules.Length > MaximumCapsules)
            throw new InvalidDataException("Unsupported or invalid Capsule document.");

        var capsules = new List<ContextCapsule>(document.Capsules.Length);
        try
        {
            foreach (var entry in document.Capsules)
            {
                if (entry is null || entry.Paths is null)
                    throw new InvalidDataException("Invalid Capsule entry.");
                capsules.Add(ContextCapsule.Capture(entry.Name, entry.Paths));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException("Invalid Capsule references or names.", ex);
        }
        return capsules.AsReadOnly();
    }
}
