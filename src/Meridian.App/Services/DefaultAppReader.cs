using System.Runtime.InteropServices;

namespace Meridian.Services;

/// <summary>
/// Looks up the friendly name of the app Windows would open a file type
/// with, for the details inspector's "Opens in" row. Read-only: it asks the
/// shell association service and changes nothing. When Windows has no
/// association (or will not say), the result is null and the row explains
/// that instead of guessing.
/// </summary>
internal static class DefaultAppReader
{
    public static string? TryGetFriendlyAppName(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension)) return null;
        try
        {
            uint length = 0;
            AssocQueryString(AssocF.None, AssocStr.FriendlyAppName, extension, null, null, ref length);
            if (length == 0) return null;
            var buffer = new char[length];
            if (AssocQueryString(AssocF.None, AssocStr.FriendlyAppName, extension, null, buffer, ref length) != 0)
                return null;
            var name = new string(buffer, 0, (int)length).TrimEnd('\0').Trim();
            return name.Length == 0 ? null : name;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    [Flags]
    private enum AssocF : uint
    {
        None = 0
    }

    private enum AssocStr
    {
        FriendlyAppName = 4
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryString(AssocF flags, AssocStr str, string pszAssoc, string? pszExtra, char[]? pszOut, ref uint pcchOut);
}
