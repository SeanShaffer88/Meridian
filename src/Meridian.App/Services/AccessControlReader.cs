using System.Security.AccessControl;
using System.Security.Principal;
using Meridian.Services;

namespace Meridian.Services;

/// <summary>
/// Reads an item's Windows access list into Meridian's plain summary
/// records. Strictly read-only: Meridian displays who can open an item and
/// hands changes off to the item's Windows Properties dialog. When Windows
/// will not show the list (protected system items are the common case), the
/// summary carries a plain explanation instead of partial data.
/// </summary>
internal static class AccessControlReader
{
    public static AccessControlSummary Read(string path, bool isDirectory)
    {
        try
        {
            FileSystemSecurity security = isDirectory
                ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                : new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);

            string? owner = null;
            try
            {
                owner = security.GetOwner(typeof(NTAccount))?.Value;
            }
            catch (IdentityNotMappedException)
            {
                try { owner = security.GetOwner(typeof(SecurityIdentifier))?.Value; }
                catch (IdentityNotMappedException) { /* owner stays unreadable */ }
            }

            var entries = new List<AccessEntry>();
            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(NTAccount));
            foreach (var rule in rules)
            {
                if (rule is not FileSystemAccessRule accessRule) continue;
                entries.Add(new AccessEntry(
                    accessRule.IdentityReference.Value,
                    accessRule.AccessControlType == AccessControlType.Allow,
                    accessRule.IsInherited,
                    MapRights(accessRule.FileSystemRights)));
            }

            return new AccessControlSummary(owner, entries, Problem: null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PrivilegeNotHeldException or
            IOException or PlatformNotSupportedException or System.Security.SecurityException or IdentityNotMappedException)
        {
            return new AccessControlSummary(
                Owner: null,
                Entries: [],
                Problem: "Windows doesn't show the access list for this item to Meridian. " +
                    "This often happens with protected system items. You can see it in the item's " +
                    "Windows Properties, on the Security tab.");
        }
    }

    private static AccessRights MapRights(FileSystemRights rights)
    {
        if ((rights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
            return AccessRights.FullControl;
        var mapped = AccessRights.None;
        if ((rights & (FileSystemRights.ReadData | FileSystemRights.ListDirectory |
            FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadAttributes)) != 0)
            mapped |= AccessRights.Read;
        if ((rights & (FileSystemRights.WriteData | FileSystemRights.CreateFiles |
            FileSystemRights.AppendData | FileSystemRights.CreateDirectories |
            FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes)) != 0)
            mapped |= AccessRights.Write;
        if ((rights & (FileSystemRights.ExecuteFile | FileSystemRights.Traverse)) != 0)
            mapped |= AccessRights.Execute;
        if ((rights & (FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles)) != 0)
            mapped |= AccessRights.Delete;
        if ((rights & FileSystemRights.ChangePermissions) != 0)
            mapped |= AccessRights.ChangePermissions;
        if ((rights & FileSystemRights.TakeOwnership) != 0)
            mapped |= AccessRights.TakeOwnership;
        return mapped;
    }
}
