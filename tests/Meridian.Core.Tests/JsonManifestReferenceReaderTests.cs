using Meridian.Core.Foresight;

namespace Meridian.Core.Tests;

public class JsonManifestReferenceReaderTests
{
    [Fact]
    public async Task ExtractsOnlyOptedInProperty()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var manifest = Path.Combine(directory, "project.json");
            await File.WriteAllTextAsync(manifest,
                """{"asset":"images/logo.png","note":"secret.txt","nested":{"asset":"sound.wav"}}""");
            var edges = await new JsonManifestReferenceReader().ReadAsync(manifest, new[] { "asset" });
            Assert.Equal(2, edges.Count);
            Assert.All(edges, edge => Assert.StartsWith(directory, edge.ReferencedPath));
            Assert.DoesNotContain(edges, edge => edge.ReferencedPath.EndsWith("secret.txt"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task RejectsTraversalReference()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var manifest = Path.Combine(directory, "project.json");
            await File.WriteAllTextAsync(manifest, """{"asset":"../outside.txt"}""");
            Assert.Empty(await new JsonManifestReferenceReader().ReadAsync(manifest, new[] { "asset" }));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task RejectsOversizedManifest()
    {
        var manifest = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(manifest, """{"asset":"a"}""");
            await Assert.ThrowsAsync<IOException>(() =>
                new JsonManifestReferenceReader().ReadAsync(manifest, new[] { "asset" }, maxBytes: 2));
        }
        finally { File.Delete(manifest); }
    }
}
