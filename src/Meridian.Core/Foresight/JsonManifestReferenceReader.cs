using System.Text.Json;

namespace Meridian.Core.Foresight;

/// <summary>
/// Reads only an explicitly selected JSON manifest. Extracts string values from
/// caller-named properties; no arbitrary JSON string is treated as a dependency.
/// </summary>
public sealed class JsonManifestReferenceReader
{
    public async Task<IReadOnlyList<DependencyEdge>> ReadAsync(
        string manifestPath,
        IReadOnlyCollection<string> referencePropertyNames,
        long maxBytes = 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(referencePropertyNames);
        if (!Path.IsPathFullyQualified(manifestPath))
            throw new ArgumentException("Manifest path must be absolute.", nameof(manifestPath));
        if (maxBytes is < 1 or > 16 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (referencePropertyNames.Count is < 1 or > 64 ||
            referencePropertyNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Provide 1 to 64 explicit property names.", nameof(referencePropertyNames));

        cancellationToken.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(manifestPath);
        if ((File.GetAttributes(full) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("Manifest must be a regular file.");
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read,
            FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maxBytes)
            throw new IOException("Manifest exceeds read limit.");
        using var document = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 32, AllowTrailingCommas = false },
            cancellationToken);
        var names = new HashSet<string>(referencePropertyNames, StringComparer.Ordinal);
        var results = new List<DependencyEdge>();
        Walk(document.RootElement, full, Path.GetDirectoryName(full)!, names, results, cancellationToken);
        return Array.AsReadOnly(results.ToArray());
    }

    private static void Walk(JsonElement element, string manifest, string directory,
        HashSet<string> names, List<DependencyEdge> results, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (results.Count >= 10000)
            throw new IOException("Reference limit exceeded.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                token.ThrowIfCancellationRequested();
                if (names.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value) &&
                        !Path.IsPathRooted(value) && !value.Contains(':') &&
                        !value.Split('/', '\\').Any(p => p is "." or ".."))
                    {
                        var resolved = Path.GetFullPath(Path.Combine(directory,
                            value.Replace('/', Path.DirectorySeparatorChar)));
                        results.Add(new DependencyEdge(manifest, resolved,
                            "JSON property: " + property.Name));
                    }
                }
                Walk(property.Value, manifest, directory, names, results, token);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                Walk(item, manifest, directory, names, results, token);
        }
    }
}
