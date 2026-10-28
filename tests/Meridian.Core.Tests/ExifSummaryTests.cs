using System.Text;
using Meridian.Core.Viewer;

namespace Meridian.Core.Tests;

public sealed class ExifSummaryTests
{
    [Fact]
    public void TryRead_JpegWithExif_ReturnsCameraDateDimensionsAndCoordinates()
    {
        var jpeg = WrapAsJpeg(BuildSampleTiff(littleEndian: true), sofWidth: 2000, sofHeight: 3000);

        Assert.True(ExifReader.TryRead(jpeg, out var summary));
        Assert.Equal("Canon", summary.CameraMake);
        Assert.Equal("Canon EOS R5", summary.CameraModel);
        // EXIF dimensions win over the JPEG frame header (2000 x 3000).
        Assert.Equal(6000, summary.PixelWidth);
        Assert.Equal(4000, summary.PixelHeight);
        // DateTimeOriginal (ExifIFD) wins over the IFD0 DateTime.
        Assert.Equal(new DateTimeOffset(2023, 11, 2, 8, 15, 30, TimeSpan.Zero), summary.DateTaken);
        Assert.NotNull(summary.Latitude);
        Assert.NotNull(summary.Longitude);
        Assert.Equal(47.6062, summary.Latitude!.Value, precision: 4);
        Assert.Equal(-122.3321, summary.Longitude!.Value, precision: 4);
        Assert.Equal("47.6062° N, 122.3321° W", summary.FormatCoordinates());
    }

    [Fact]
    public void TryRead_BigEndianTiff_ReturnsTheSameFacts()
    {
        var tiff = BuildSampleTiff(littleEndian: false);

        Assert.True(ExifReader.TryRead(tiff, out var summary));
        Assert.Equal("Canon", summary.CameraMake);
        Assert.Equal("Canon EOS R5", summary.CameraModel);
        Assert.Equal(6000, summary.PixelWidth);
        Assert.Equal(new DateTimeOffset(2023, 11, 2, 8, 15, 30, TimeSpan.Zero), summary.DateTaken);
        Assert.Equal("47.6062° N, 122.3321° W", summary.FormatCoordinates());
    }

    [Fact]
    public void TryRead_JpegWithoutExif_FallsBackToFrameHeaderDimensions()
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]);
        // SOF0: length 11, precision 8, height 1080, width 1920, one component.
        stream.Write([0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x04, 0x38, 0x07, 0x80, 0x01, 0x01, 0x11, 0x00]);
        stream.Write([0xFF, 0xDA]);

        Assert.True(ExifReader.TryRead(stream.ToArray(), out var summary));
        Assert.Equal(1920, summary.PixelWidth);
        Assert.Equal(1080, summary.PixelHeight);
        Assert.Null(summary.CameraMake);
        Assert.Null(summary.FormatCoordinates());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(64)]
    public void TryRead_TruncatedExif_NeverThrows(int keepBytes)
    {
        var jpeg = WrapAsJpeg(BuildSampleTiff(littleEndian: true), sofWidth: 2000, sofHeight: 3000);
        var truncated = jpeg.AsSpan(0, Math.Min(keepBytes, jpeg.Length)).ToArray();

        var read = ExifReader.TryRead(truncated, out var summary);

        Assert.NotNull(summary);
        if (!read) Assert.False(summary.HasAnyData);
    }

    [Fact]
    public void TryRead_Garbage_ReturnsFalse()
    {
        var garbage = Encoding.ASCII.GetBytes("this is not an image at all, just text pretending");
        Assert.False(ExifReader.TryRead(garbage, out var summary));
        Assert.False(summary.HasAnyData);
        Assert.False(ExifReader.TryRead(ReadOnlySpan<byte>.Empty, out _));
        Assert.False(ExifReader.TryRead(new byte[] { 0xFF, 0xD8 }, out _));
    }

    [Fact]
    public void TryRead_ExifWithImpossibleCoordinates_DropsOnlyTheCoordinates()
    {
        // Latitude of 100 degrees cannot exist; the rest of the summary survives.
        var tiff = BuildSampleTiff(littleEndian: true, latitudeDegrees: 100);
        Assert.True(ExifReader.TryRead(tiff, out var summary));
        Assert.Equal("Canon", summary.CameraMake);
        Assert.Null(summary.Latitude);
        Assert.Null(summary.FormatCoordinates());
    }

    /// <summary>
    /// Builds one fixed sample TIFF: Make "Canon", Model "Canon EOS R5",
    /// IFD0 date 2024-05-01 12:30:45, DateTimeOriginal 2023-11-02 08:15:30,
    /// dimensions 6000 x 4000, GPS at the given latitude degrees / 36' 22.32"
    /// N and 122 / 19' 55.56" W.
    /// </summary>
    private static byte[] BuildSampleTiff(bool littleEndian, int latitudeDegrees = 47)
    {
        var bytes = new List<byte>();

        void Put16(ushort value)
        {
            if (littleEndian) { bytes.Add((byte)value); bytes.Add((byte)(value >> 8)); }
            else { bytes.Add((byte)(value >> 8)); bytes.Add((byte)value); }
        }

        void Put32(uint value)
        {
            if (littleEndian)
            {
                bytes.Add((byte)value); bytes.Add((byte)(value >> 8));
                bytes.Add((byte)(value >> 16)); bytes.Add((byte)(value >> 24));
            }
            else
            {
                bytes.Add((byte)(value >> 24)); bytes.Add((byte)(value >> 16));
                bytes.Add((byte)(value >> 8)); bytes.Add((byte)value);
            }
        }

        byte[] Field32(uint value)
        {
            var field = new byte[4];
            if (littleEndian)
            {
                field[0] = (byte)value; field[1] = (byte)(value >> 8);
                field[2] = (byte)(value >> 16); field[3] = (byte)(value >> 24);
            }
            else
            {
                field[0] = (byte)(value >> 24); field[1] = (byte)(value >> 16);
                field[2] = (byte)(value >> 8); field[3] = (byte)value;
            }

            return field;
        }

        void Entry(ushort tag, ushort type, uint count, byte[] valueField)
        {
            Put16(tag);
            Put16(type);
            Put32(count);
            bytes.AddRange(valueField);
        }

        void Ifd((ushort Tag, ushort Type, uint Count, byte[] Value)[] entries)
        {
            Put16((ushort)entries.Length);
            foreach (var (tag, type, count, value) in entries) Entry(tag, type, count, value);
            Put32(0); // no next IFD
        }

        // Fixed layout: header 8 bytes; IFD0 (5 entries) at 8, size 66;
        // ExifIFD (3 entries) at 74, size 42; GPS IFD (4 entries) at 116,
        // size 54; data area at 170.
        const int exifIfdOffset = 74;
        const int gpsIfdOffset = 116;
        const int dataOffset = 170;
        const int makeOffset = dataOffset;        // 6 bytes
        const int modelOffset = makeOffset + 6;   // 13 bytes
        const int dateOffset = modelOffset + 13;  // 20 bytes
        const int originalOffset = dateOffset + 20; // 20 bytes
        const int latitudeOffset = originalOffset + 20; // 24 bytes
        const int longitudeOffset = latitudeOffset + 24; // 24 bytes

        if (littleEndian) { bytes.Add((byte)'I'); bytes.Add((byte)'I'); }
        else { bytes.Add((byte)'M'); bytes.Add((byte)'M'); }
        Put16(42);
        Put32(8);

        Ifd([
            (0x010F, 2, 6, Field32(makeOffset)),
            (0x0110, 2, 13, Field32(modelOffset)),
            (0x0132, 2, 20, Field32(dateOffset)),
            (0x8769, 4, 1, Field32(exifIfdOffset)),
            (0x8825, 4, 1, Field32(gpsIfdOffset))
        ]);
        Ifd([
            (0x9003, 2, 20, Field32(originalOffset)),
            (0xA002, 4, 1, Field32(6000)),
            (0xA003, 4, 1, Field32(4000))
        ]);
        Ifd([
            (0x0001, 2, 2, [(byte)'N', 0, 0, 0]),
            (0x0002, 5, 3, Field32(latitudeOffset)),
            (0x0003, 2, 2, [(byte)'W', 0, 0, 0]),
            (0x0004, 5, 3, Field32(longitudeOffset))
        ]);

        Assert.Equal(dataOffset, bytes.Count);
        bytes.AddRange(Encoding.ASCII.GetBytes("Canon\0"));
        bytes.AddRange(Encoding.ASCII.GetBytes("Canon EOS R5\0"));
        bytes.AddRange(Encoding.ASCII.GetBytes("2024:05:01 12:30:45\0"));
        bytes.AddRange(Encoding.ASCII.GetBytes("2023:11:02 08:15:30\0"));
        foreach (var (numerator, denominator) in new[] { ((uint)latitudeDegrees, 1u), (36u, 1u), (2232u, 100u) })
        {
            Put32(numerator);
            Put32(denominator);
        }

        foreach (var (numerator, denominator) in new[] { (122u, 1u), (19u, 1u), (5556u, 100u) })
        {
            Put32(numerator);
            Put32(denominator);
        }

        return bytes.ToArray();
    }

    private static byte[] WrapAsJpeg(byte[] tiff, ushort sofWidth, ushort sofHeight)
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]);
        var app1Length = 2 + 6 + tiff.Length;
        stream.Write([0xFF, 0xE1, (byte)(app1Length >> 8), (byte)(app1Length & 0xFF)]);
        stream.Write(Encoding.ASCII.GetBytes("Exif\0\0"));
        stream.Write(tiff);
        // SOF0 with one component.
        stream.Write([0xFF, 0xC0, 0x00, 0x0B, 0x08,
            (byte)(sofHeight >> 8), (byte)(sofHeight & 0xFF),
            (byte)(sofWidth >> 8), (byte)(sofWidth & 0xFF),
            0x01, 0x01, 0x11, 0x00]);
        stream.Write([0xFF, 0xDA]);
        return stream.ToArray();
    }
}
