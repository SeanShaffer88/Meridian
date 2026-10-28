using System.Buffers.Binary;
using System.Globalization;

namespace Meridian.Core.Viewer;

/// <summary>
/// The photo facts Meridian can read from an image file's own metadata:
/// pixel dimensions, camera make and model, the date the photo was taken,
/// and GPS coordinates. Coordinates are data for display as text only;
/// nothing here opens a map or touches the network.
/// </summary>
public sealed record ExifSummary(
    int? PixelWidth,
    int? PixelHeight,
    string? CameraMake,
    string? CameraModel,
    DateTimeOffset? DateTaken,
    double? Latitude,
    double? Longitude)
{
    public bool HasAnyData =>
        PixelWidth is not null || PixelHeight is not null ||
        CameraMake is not null || CameraModel is not null ||
        DateTaken is not null || Latitude is not null || Longitude is not null;

    public bool HasCoordinates => Latitude is not null && Longitude is not null;

    /// <summary>"47.6062° N, 122.3321° W" — or null when no coordinates were recorded.</summary>
    public string? FormatCoordinates()
    {
        if (Latitude is not { } latitude || Longitude is not { } longitude) return null;
        var latitudeText = Math.Abs(latitude).ToString("0.0000", CultureInfo.InvariantCulture) +
            (latitude >= 0 ? "° N" : "° S");
        var longitudeText = Math.Abs(longitude).ToString("0.0000", CultureInfo.InvariantCulture) +
            (longitude >= 0 ? "° E" : "° W");
        return latitudeText + ", " + longitudeText;
    }
}

/// <summary>
/// A small, defensive EXIF reader for JPEG and TIFF data. It understands the
/// layout cameras actually write (TIFF header, IFD0, the EXIF and GPS
/// sub-IFDs) and the JPEG frame header for dimensions when EXIF carries
/// none. Malformed or truncated data yields whatever could be read safely —
/// this reader never throws on bad input, because it runs on arbitrary
/// user files.
/// </summary>
public static class ExifReader
{
    private const int MaximumIfdEntries = 512;
    private const int MaximumAsciiLength = 512;

    public static bool TryRead(ReadOnlySpan<byte> data, out ExifSummary summary)
    {
        summary = new ExifSummary(null, null, null, null, null, null, null);
        if (data.Length < 4) return false;

        int? width = null;
        int? height = null;
        ExifSummary? fromTiff = null;

        if (data[0] == 0xFF && data[1] == 0xD8)
        {
            // JPEG: walk the marker segments looking for the EXIF APP1 block
            // and a start-of-frame marker for the coded dimensions.
            var offset = 2;
            while (offset + 4 <= data.Length)
            {
                if (data[offset] != 0xFF) break;
                var marker = data[offset + 1];
                if (marker == 0xDA || marker == 0xD9) break; // scan data / end of image
                var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 2, 2));
                if (segmentLength < 2) break;
                var segmentData = offset + 4;
                var segmentEnd = offset + 2 + segmentLength;
                if (segmentEnd > data.Length) break;
                if (marker == 0xE1 && segmentLength >= 8 &&
                    data.Slice(segmentData, 6).SequenceEqual("Exif\0\0"u8))
                {
                    fromTiff = ParseTiff(data.Slice(segmentData + 6, segmentLength - 8));
                }
                else if (IsStartOfFrame(marker) && segmentLength >= 7)
                {
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(segmentData + 1, 2));
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(segmentData + 3, 2));
                }

                offset = segmentEnd;
            }
        }
        else if (IsTiffHeader(data))
        {
            fromTiff = ParseTiff(data);
        }

        if (fromTiff is null)
        {
            summary = new ExifSummary(width, height, null, null, null, null, null);
            return summary.HasAnyData;
        }
        summary = fromTiff with
        {
            PixelWidth = fromTiff.PixelWidth ?? width,
            PixelHeight = fromTiff.PixelHeight ?? height
        };
        return summary.HasAnyData;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static bool IsTiffHeader(ReadOnlySpan<byte> data) =>
        data.Length >= 8 &&
        ((data[0] == (byte)'I' && data[1] == (byte)'I' && data[2] == 0x2A && data[3] == 0x00) ||
         (data[0] == (byte)'M' && data[1] == (byte)'M' && data[2] == 0x00 && data[3] == 0x2A));

    private static ExifSummary? ParseTiff(ReadOnlySpan<byte> tiff)
    {
        if (!IsTiffHeader(tiff)) return null;
        var littleEndian = tiff[0] == (byte)'I';
        var ifd0Offset = ReadUInt32(tiff, 4, littleEndian);
        if (ifd0Offset is not { } start || start >= (uint)tiff.Length) return null;

        string? make = null;
        string? model = null;
        DateTimeOffset? dateTaken = null;
        int? width = null;
        int? height = null;
        double? latitude = null;
        double? longitude = null;

        foreach (var entry in ReadIfd(tiff, start, littleEndian))
        {
            switch (entry.Tag)
            {
                case 0x010F: make = ReadAscii(tiff, entry, littleEndian); break;
                case 0x0110: model = ReadAscii(tiff, entry, littleEndian); break;
                case 0x0132: dateTaken = ParseExifDate(ReadAscii(tiff, entry, littleEndian)) ?? dateTaken; break;
                case 0x8769 when ReadUInt32Value(tiff, entry, littleEndian) is { } exifOffset:
                    foreach (var sub in ReadIfd(tiff, exifOffset, littleEndian))
                    {
                        switch (sub.Tag)
                        {
                            case 0x9003: dateTaken = ParseExifDate(ReadAscii(tiff, sub, littleEndian)) ?? dateTaken; break;
                            case 0xA002: width = ReadIntValue(tiff, sub, littleEndian) ?? width; break;
                            case 0xA003: height = ReadIntValue(tiff, sub, littleEndian) ?? height; break;
                        }
                    }

                    break;
                case 0x8825 when ReadUInt32Value(tiff, entry, littleEndian) is { } gpsOffset:
                    string? latitudeRef = null;
                    string? longitudeRef = null;
                    double? latitudeValue = null;
                    double? longitudeValue = null;
                    foreach (var sub in ReadIfd(tiff, gpsOffset, littleEndian))
                    {
                        switch (sub.Tag)
                        {
                            case 0x0001: latitudeRef = ReadAscii(tiff, sub, littleEndian); break;
                            case 0x0002: latitudeValue = ReadCoordinate(tiff, sub, littleEndian); break;
                            case 0x0003: longitudeRef = ReadAscii(tiff, sub, littleEndian); break;
                            case 0x0004: longitudeValue = ReadCoordinate(tiff, sub, littleEndian); break;
                        }
                    }

                    if (latitudeValue is { } lat && longitudeValue is { } lon &&
                        Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180)
                    {
                        latitude = string.Equals(latitudeRef, "S", StringComparison.OrdinalIgnoreCase) ? -lat : lat;
                        longitude = string.Equals(longitudeRef, "W", StringComparison.OrdinalIgnoreCase) ? -lon : lon;
                    }

                    break;
            }
        }

        var summary = new ExifSummary(width, height, Clean(make), Clean(model), dateTaken, latitude, longitude);
        return summary.HasAnyData ? summary : null;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim().TrimEnd('\0').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static DateTimeOffset? ParseExifDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // EXIF dates are "yyyy:MM:dd HH:mm:ss" in the camera's local time;
        // the zone is not recorded, so the value is presented as recorded.
        if (DateTime.TryParseExact(value.Trim(), "yyyy:MM:dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return new DateTimeOffset(parsed, TimeSpan.Zero);
        return null;
    }

    private readonly record struct IfdEntry(ushort Tag, ushort Type, uint Count, uint ValueOffset);

    private static List<IfdEntry> ReadIfd(ReadOnlySpan<byte> tiff, uint offset, bool littleEndian)
    {
        var entries = new List<IfdEntry>();
        if (offset + 2 > (uint)tiff.Length) return entries;
        var count = ReadUInt16(tiff, (int)offset, littleEndian);
        if (count is not { } entryCount || entryCount > MaximumIfdEntries) return entries;
        for (var i = 0; i < entryCount; i++)
        {
            var entryOffset = (long)offset + 2 + (long)i * 12;
            if (entryOffset + 12 > tiff.Length) break;
            var tag = ReadUInt16(tiff, (int)entryOffset, littleEndian);
            var type = ReadUInt16(tiff, (int)entryOffset + 2, littleEndian);
            var valueCount = ReadUInt32(tiff, (int)entryOffset + 4, littleEndian);
            var valueOffset = ReadUInt32(tiff, (int)entryOffset + 8, littleEndian);
            if (tag is null || type is null || valueCount is null || valueOffset is null) break;
            entries.Add(new IfdEntry(tag.Value, type.Value, valueCount.Value, valueOffset.Value));
        }

        return entries;
    }

    private static int TypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,   // BYTE, ASCII, SBYTE, UNDEFINED
        3 or 8 => 2,             // SHORT, SSHORT
        4 or 9 or 11 => 4,       // LONG, SLONG, FLOAT
        5 or 10 or 12 => 8,      // RATIONAL, SRATIONAL, DOUBLE
        _ => 0
    };

    /// <summary>The span holding an entry's value: inline in the entry when it fits, else at the stored offset.</summary>
    private static ReadOnlySpan<byte> ValueSpan(ReadOnlySpan<byte> tiff, IfdEntry entry, bool littleEndian)
    {
        var size = TypeSize(entry.Type);
        if (size == 0 || entry.Count > 1_000_000) return ReadOnlySpan<byte>.Empty;
        var total = (long)size * entry.Count;
        if (total <= 4)
        {
            // The value sits in the entry's own 4-byte field, which ReadIfd
            // exposed as ValueOffset; recover its bytes in file order.
            Span<byte> inline = stackalloc byte[4];
            if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(inline, entry.ValueOffset);
            else BinaryPrimitives.WriteUInt32BigEndian(inline, entry.ValueOffset);
            return inline.Slice(0, (int)total).ToArray();
        }

        if (entry.ValueOffset >= (uint)tiff.Length || entry.ValueOffset + total > tiff.Length)
            return ReadOnlySpan<byte>.Empty;
        return tiff.Slice((int)entry.ValueOffset, (int)total);
    }

    private static string? ReadAscii(ReadOnlySpan<byte> tiff, IfdEntry entry, bool littleEndian)
    {
        if (entry.Type != 2) return null;
        var span = ValueSpan(tiff, entry, littleEndian);
        if (span.Length == 0) return null;
        var length = Math.Min(span.Length, MaximumAsciiLength);
        var end = span.Slice(0, length).IndexOf((byte)0);
        if (end >= 0) length = end;
        return System.Text.Encoding.ASCII.GetString(span.Slice(0, length));
    }

    private static uint? ReadUInt32Value(ReadOnlySpan<byte> tiff, IfdEntry entry, bool littleEndian)
    {
        var span = ValueSpan(tiff, entry, littleEndian);
        if (span.Length < 4) return null;
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(span)
            : BinaryPrimitives.ReadUInt32BigEndian(span);
    }

    private static int? ReadIntValue(ReadOnlySpan<byte> tiff, IfdEntry entry, bool littleEndian)
    {
        var span = ValueSpan(tiff, entry, littleEndian);
        long? value = entry.Type switch
        {
            3 when span.Length >= 2 => littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                : BinaryPrimitives.ReadUInt16BigEndian(span),
            4 when span.Length >= 4 => littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(span)
                : BinaryPrimitives.ReadUInt32BigEndian(span),
            9 when span.Length >= 4 => littleEndian
                ? BinaryPrimitives.ReadInt32LittleEndian(span)
                : BinaryPrimitives.ReadInt32BigEndian(span),
            _ => null
        };
        if (value is not { } result || result is < 0 or > int.MaxValue) return null;
        return (int)result;
    }

    private static double? ReadCoordinate(ReadOnlySpan<byte> tiff, IfdEntry entry, bool littleEndian)
    {
        // GPS coordinates are three rationals: degrees, minutes, seconds.
        if (entry.Type != 5 || entry.Count < 3) return null;
        var span = ValueSpan(tiff, entry, littleEndian);
        if (span.Length < 24) return null;
        return ReadRational(span, 0, littleEndian) +
               ReadRational(span, 1, littleEndian) / 60.0 +
               ReadRational(span, 2, littleEndian) / 3600.0;
    }

    private static double ReadRational(ReadOnlySpan<byte> span, int index, bool littleEndian)
    {
        var numerator = littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(index * 8, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(index * 8, 4));
        var denominator = littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(index * 8 + 4, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(index * 8 + 4, 4));
        return denominator == 0 ? 0 : (double)numerator / denominator;
    }

    private static ushort? ReadUInt16(ReadOnlySpan<byte> data, int offset, bool littleEndian)
    {
        if (offset < 0 || offset + 2 > data.Length) return null;
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    }

    private static uint? ReadUInt32(ReadOnlySpan<byte> data, int offset, bool littleEndian)
    {
        if (offset < 0 || offset + 4 > data.Length) return null;
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    }
}
