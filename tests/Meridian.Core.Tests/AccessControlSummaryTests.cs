using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class AccessControlSummaryTests
{
    [Theory]
    [InlineData(AccessRights.None, "No access")]
    [InlineData(AccessRights.FullControl, "Full control")]
    [InlineData(AccessRights.FullControl | AccessRights.Read, "Full control")]
    [InlineData(AccessRights.Read, "Read")]
    [InlineData(AccessRights.Read | AccessRights.Write, "Read, Write")]
    [InlineData(AccessRights.Read | AccessRights.Execute | AccessRights.Delete, "Read, Open / run, Delete")]
    [InlineData(AccessRights.ChangePermissions | AccessRights.TakeOwnership, "Change permissions, Take ownership")]
    public void RightsLabel_UsesPlainWordsInFixedOrder(AccessRights rights, string expected)
    {
        Assert.Equal(expected, AccessControlSummaryFormatter.RightsLabel(rights));
    }

    [Fact]
    public void DescribeLines_ShowsOwnerEntriesAndInheritance()
    {
        var summary = new AccessControlSummary(
            "STUDIO\\Sean",
            [
                new AccessEntry("STUDIO\\Sean", IsAllow: true, IsInherited: false, AccessRights.FullControl),
                new AccessEntry("BUILTIN\\Users", IsAllow: true, IsInherited: true, AccessRights.Read | AccessRights.Execute),
                new AccessEntry("STUDIO\\Guest", IsAllow: false, IsInherited: false, AccessRights.Write)
            ],
            Problem: null);

        var lines = AccessControlSummaryFormatter.DescribeLines(summary);

        Assert.Equal(
            [
                "Owner: STUDIO\\Sean",
                "STUDIO\\Sean — Allow — Full control",
                "BUILTIN\\Users — Allow — Read, Open / run (inherited)",
                "STUDIO\\Guest — Deny — Write"
            ],
            lines);
    }

    [Fact]
    public void DescribeLines_ProblemExplainsInsteadOfListingEntries()
    {
        var summary = new AccessControlSummary(
            Owner: null,
            Entries: [],
            Problem: "Windows doesn't show the access list for this item.");

        var lines = AccessControlSummaryFormatter.DescribeLines(summary);

        Assert.Equal(
            ["Owner: could not be read", "Windows doesn't show the access list for this item."],
            lines);
    }

    [Fact]
    public void DescribeLines_NoRulesReported_SaysSo()
    {
        var summary = new AccessControlSummary("NT AUTHORITY\\SYSTEM", [], Problem: null);

        var lines = AccessControlSummaryFormatter.DescribeLines(summary);

        Assert.Equal(
            ["Owner: NT AUTHORITY\\SYSTEM", "Windows did not report any access rules for this item."],
            lines);
    }
}
