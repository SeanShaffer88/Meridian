using Meridian.Services;

namespace Meridian.Tests;

public sealed class DriveInspectorTests
{
    private sealed class FakeFactsSource(DriveFacts facts) : IDriveFactsSource
    {
        public DriveFacts Read(string rootPath) => facts;
    }

    private static IReadOnlyList<string> Describe(DriveFacts facts) =>
        DriveInspector.Describe(new FakeFactsSource(facts).Read(facts.RootPath));

    [Fact]
    public void Describe_ReadyFixedDrive_ShowsTheReportedFacts()
    {
        const long total = 500L * 1024 * 1024 * 1024;
        const long free = 125L * 1024 * 1024 * 1024;
        var lines = Describe(new DriveFacts(@"C:\", "System", DriveType.Fixed, "NTFS", total, free, true));

        Assert.Contains("Volume label: System", lines);
        Assert.Contains(@"Drive: C:\", lines);
        Assert.Contains("Type: Local disk", lines);
        Assert.Contains("File system: NTFS", lines);
        Assert.Contains($"Capacity: {Meridian.Services.DirectoryReader.FormatSize(total)}", lines);
        Assert.Contains(lines, line => line.StartsWith("Free space: ", StringComparison.Ordinal) && line.Contains("free of"));
        Assert.Contains("State: Ready", lines);
        // No invented health claim anywhere in the report.
        Assert.DoesNotContain(lines, line => line.Contains("health", StringComparison.OrdinalIgnoreCase) || line.Contains("SMART", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Describe_RemovableDrive_IsLabelledRemovable()
    {
        var lines = Describe(new DriveFacts(@"E:\", "", DriveType.Removable, "exFAT", 32_000_000_000, 8_000_000_000, true));
        Assert.Contains("Type: Removable", lines);
        Assert.Contains("File system: exFAT", lines);
        Assert.Contains("Volume label: —", lines);
    }

    [Fact]
    public void Describe_NotReadyDrive_ReportsPlainlyWithoutThrowing()
    {
        var lines = Describe(new DriveFacts(@"F:\", "", DriveType.Removable, "", 0, 0, false));

        Assert.Contains("File system: Not available", lines);
        Assert.Contains("Capacity: Not available", lines);
        Assert.Contains("Free space: Not available", lines);
        Assert.Contains(lines, line => line.StartsWith("State: Not ready", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DriveType.Fixed, "Local disk")]
    [InlineData(DriveType.Removable, "Removable")]
    [InlineData(DriveType.Network, "Network drive")]
    [InlineData(DriveType.CDRom, "Disc drive")]
    [InlineData(DriveType.Ram, "RAM disk")]
    [InlineData(DriveType.Unknown, "Drive")]
    public void TypeLabel_MatchesTheAppsWording(DriveType type, string expected)
    {
        Assert.Equal(expected, DriveInspector.TypeLabel(type));
    }

    [Fact]
    public void FromDriveInfo_ReadsTheRealTempDrive()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var facts = DriveInspector.FromDriveInfo(new DriveInfo(root));

        Assert.Equal(root, facts.RootPath);
        Assert.True(facts.IsReady);
        Assert.True(facts.TotalBytes > 0);
        Assert.InRange(facts.FreeBytes, 0, facts.TotalBytes);
        Assert.False(string.IsNullOrWhiteSpace(facts.DriveFormat));
    }
}
