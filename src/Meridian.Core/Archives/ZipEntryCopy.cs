using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>Copies a complete ZIP file entry while enforcing its size and CRC.</summary>
internal static class ZipEntryCopy
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    internal static async Task<long> CopyAsync(ZipArchiveEntry entry, Stream output,
        long maxBytes, CancellationToken cancellationToken)
    {
        if (entry.Length > maxBytes)
            throw new InvalidDataException("ZIP entry exceeds extraction budget.");
        await using var input = entry.Open();
        var buffer = new byte[8192];
        long total = 0;
        uint crc = uint.MaxValue;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (read > entry.Length - total || read > maxBytes - total)
                throw new InvalidDataException("ZIP data exceeds declared size or extraction budget.");
            for (var i = 0; i < read; i++)
                crc = CrcTable[(crc ^ buffer[i]) & 0xff] ^ (crc >> 8);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (total != entry.Length || ~crc != entry.Crc32)
            throw new InvalidDataException("ZIP entry size or CRC does not match its metadata.");
        return total;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xedb88320U ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
