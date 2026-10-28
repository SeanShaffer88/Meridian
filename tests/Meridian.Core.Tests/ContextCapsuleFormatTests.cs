using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class ContextCapsuleFormatTests
{
    [Fact]
    public void RoundTrip_PreservesOfflineReferences()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"unavailable-{Guid.NewGuid():N}");
        var original = ContextCapsule.Capture("Project", new[] { missing });
        var result = Assert.Single(ContextCapsuleFormat.Deserialize(
            ContextCapsuleFormat.Serialize(new[] { original })));
        Assert.Equal("Project", result.Name);
        Assert.Equal(missing, Assert.Single(result.Paths));
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void Deserialize_RejectsUnknownVersion()
    {
        Assert.Throws<InvalidDataException>(() =>
            ContextCapsuleFormat.Deserialize("{\"Version\":999,\"Capsules\":[]}"));
    }

    [Fact]
    public void Deserialize_RejectsOverBudgetInput()
    {
        Assert.Throws<InvalidDataException>(() =>
            ContextCapsuleFormat.Deserialize(new string('a', ContextCapsuleFormat.MaximumDocumentCharacters + 1)));
    }

    [Fact]
    public void Deserialize_RejectsMalformedJson()
    {
        Assert.Throws<InvalidDataException>(() => ContextCapsuleFormat.Deserialize("{bad"));
    }

    [Fact]
    public void Serialize_RejectsExcessiveCapsules()
    {
        var single = ContextCapsule.Capture("Project", Array.Empty<string>());
        Assert.Throws<ArgumentException>(() =>
            ContextCapsuleFormat.Serialize(Enumerable.Repeat(single, ContextCapsuleFormat.MaximumCapsules + 1)));
    }
}
