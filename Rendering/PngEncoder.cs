using System.Buffers.Binary;
using System.IO.Compression;

namespace AStar.Rendering;

/// <summary>
/// Minimal PNG writer for 8-bit indexed-palette images (PNG colour type 3).
/// Uses only the .NET base class library: <see cref="ZLibStream"/> for the
/// IDAT payload and an inline CRC-32 for the chunk checksums. No third-party
/// dependencies, so the study can report "libraries used: none".
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// Writes <paramref name="image"/> to <paramref name="path"/>, creating the
    /// containing directory if needed. Palette entries are RGB triples; the
    /// pixel bytes are indices into that palette.
    /// </summary>
    public static void Write(string path, IndexedImage image)
    {
        if (image.Palette.Length is 0 or > 256)
            throw new ArgumentException($"Palette must hold 1-256 entries, got {image.Palette.Length}.", nameof(image));

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var file = File.Create(path);

        file.Write(Signature);
        WriteChunk(file, "IHDR", BuildHeader(image.Width, image.Height));
        WriteChunk(file, "PLTE", BuildPalette(image.Palette));
        WriteChunk(file, "IDAT", Deflate(image));
        WriteChunk(file, "IEND", Array.Empty<byte>());
    }

    private static byte[] BuildHeader(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bit depth: 8 bits per palette index
        header[9] = 3;  // colour type 3: indexed
        header[10] = 0; // compression method: deflate
        header[11] = 0; // filter method: adaptive (per-scanline filter byte)
        header[12] = 0; // interlace method: none
        return header;
    }

    private static byte[] BuildPalette((byte R, byte G, byte B)[] palette)
    {
        var plte = new byte[palette.Length * 3];
        for (int i = 0; i < palette.Length; i++)
        {
            plte[i * 3 + 0] = palette[i].R;
            plte[i * 3 + 1] = palette[i].G;
            plte[i * 3 + 2] = palette[i].B;
        }
        return plte;
    }

    /// <summary>
    /// Builds the zlib-compressed IDAT payload. Every PNG scanline is prefixed
    /// with its filter-type byte; filter 0 (None) is used throughout, which
    /// deflates well on the large flat colour areas these figures consist of.
    /// </summary>
    private static byte[] Deflate(IndexedImage image)
    {
        var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var filterByte = new byte[] { 0 };
            for (int y = 0; y < image.Height; y++)
            {
                zlib.Write(filterByte);
                zlib.Write(image.Pixels, y * image.Width, image.Width);
            }
        }
        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        output.Write(typeBytes);
        output.Write(data);

        // The CRC covers the chunk type and data, but not the length field.
        uint crc = Crc32.Compute(Crc32.Compute(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    /// <summary>Table-driven CRC-32 (the polynomial PNG mandates).</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        /// <summary>Folds <paramref name="data"/> into a running CRC register.</summary>
        public static uint Compute(uint register, byte[] data)
        {
            foreach (byte b in data)
                register = Table[(register ^ b) & 0xFF] ^ (register >> 8);
            return register;
        }
    }
}
