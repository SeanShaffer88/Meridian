using System.Text.Json;
using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class FailureLogTests
{
    [Fact]
    public void RecordsStructuredUtcExceptionDetails()
    {
        using var fixture = new Fixture();
        var error = new IOException("outer 文件", new InvalidOperationException("inner"));
        Assert.True(FailureLog.TryRecord(error, fixture.Root));
        using var json = JsonDocument.Parse(File.ReadAllBytes(fixture.Log));
        var record = json.RootElement;
        Assert.Equal(1, record.GetProperty("Version").GetInt32());
        Assert.Equal(TimeSpan.Zero, record.GetProperty("TimestampUtc").GetDateTimeOffset().Offset);
        var entries = record.GetProperty("Exceptions");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal("outer 文件", entries[0].GetProperty("Message").GetString());
        Assert.Equal(error.HResult, entries[0].GetProperty("HResult").GetInt32());
        Assert.Equal("System.IO.IOException", entries[0].GetProperty("Type").GetString());
        Assert.Equal("inner", entries[1].GetProperty("Message").GetString());
        Assert.False(record.GetProperty("ChainTruncated").GetBoolean());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void BoundsEscapedMessagesStackTracesAndExceptionChains(int depth)
    {
        using var fixture = new Fixture();
        Exception? error = null;
        for (var index = 0; index < depth; index++) error = new HugeException(error);
        Assert.True(FailureLog.TryRecord(error!, fixture.Root));
        Assert.InRange(new FileInfo(fixture.Log).Length, 1, FailureLog.MaximumBytes);
        using var json = JsonDocument.Parse(File.ReadAllBytes(fixture.Log));
        Assert.Equal(Math.Min(depth, 4), json.RootElement.GetProperty("Exceptions").GetArrayLength());
        Assert.Equal(depth > 4, json.RootElement.GetProperty("ChainTruncated").GetBoolean());
        foreach (var detail in json.RootElement.GetProperty("Exceptions").EnumerateArray())
        {
            Assert.Equal(512, detail.GetProperty("Message").GetString()!.Length);
            Assert.Equal(4096, detail.GetProperty("StackTrace").GetString()!.Length);
            Assert.True(detail.GetProperty("Truncated").GetBoolean());
        }
    }

    [Fact]
    public void DoesNotInvokeExceptionOrDataObjectFormatting()
    {
        using var fixture = new Fixture();
        var error = new HostileFormattingException();
        error.Data.Add(new HostileValue(), new HostileValue());
        Assert.True(FailureLog.TryRecord(error, fixture.Root));
        using var json = JsonDocument.Parse(File.ReadAllBytes(fixture.Log));
        Assert.Equal("safe message", json.RootElement.GetProperty("Exceptions")[0].GetProperty("Message").GetString());
    }

    [Fact]
    public void FailedPublicationPreservesPreviousLogAndCleansTemporaryFile()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Log, "previous diagnostic");
        using (var held = File.Open(fixture.Log, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(FailureLog.TryRecord(new Exception("new diagnostic"), fixture.Root));
        Assert.Equal("previous diagnostic", File.ReadAllText(fixture.Log));
        Assert.Single(Directory.GetFiles(fixture.Root));
        Assert.True(FailureLog.TryRecord(new Exception("retry"), fixture.Root));
        using var json = JsonDocument.Parse(File.ReadAllBytes(fixture.Log));
        Assert.Equal("retry", json.RootElement.GetProperty("Exceptions")[0].GetProperty("Message").GetString());
    }

    [Fact]
    public void ExistingOversizedLogIsReplacedWithBoundedRecord()
    {
        using var fixture = new Fixture();
        File.WriteAllBytes(fixture.Log, new byte[FailureLog.MaximumBytes * 2]);
        Assert.True(FailureLog.TryRecord(new Exception("replacement"), fixture.Root));
        Assert.InRange(new FileInfo(fixture.Log).Length, 1, FailureLog.MaximumBytes);
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public void UnusableDirectoryReturnsFailureWithoutChangingExistingFile()
    {
        using var fixture = new Fixture();
        var blocked = Path.Combine(fixture.Root, "file");
        File.WriteAllText(blocked, "preserve");
        Assert.False(FailureLog.TryRecord(new Exception("diagnostic"), blocked));
        Assert.Equal("preserve", File.ReadAllText(blocked));
        Assert.False(FailureLog.TryRecord(new Exception("diagnostic"), "invalid\0path"));
    }

    private sealed class HugeException(Exception? inner) : Exception(new string('\u0001', 10000), inner)
    {
        public override string StackTrace => new('\u0001', 10000);
    }
    private sealed class HostileFormattingException : Exception
    {
        public override string Message => "safe message";
        public override string ToString() => throw new InvalidOperationException("Do not format the exception.");
    }
    private sealed class HostileValue
    {
        public override string ToString() => throw new InvalidOperationException("Do not format exception data.");
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "meridian-log-" + Guid.NewGuid().ToString("N"));
        public string Log => Path.Combine(Root, "last-error.log");
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
