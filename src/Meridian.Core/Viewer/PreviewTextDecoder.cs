using System.Text;

namespace Meridian.Core.Viewer;

/// <summary>Strict bounded text decoding for preview; never executes file contents.</summary>
public static class PreviewTextDecoder
{
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out string text)
    {
        text = string.Empty;
        Encoding encoding;
        var offset = 0;
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            encoding = new UTF8Encoding(false, true);
            offset = 3;
        }
        else if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            encoding = new UnicodeEncoding(false, true, true);
            offset = 2;
        }
        else if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            encoding = new UnicodeEncoding(true, true, true);
            offset = 2;
        }
        else
        {
            encoding = new UTF8Encoding(false, true);
        }

        try
        {
            text = encoding.GetString(bytes[offset..]);
            if (text.IndexOf('\0') >= 0)
            {
                text = string.Empty;
                return false;
            }
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
