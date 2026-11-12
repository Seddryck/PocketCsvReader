using System.Buffers.Binary;
using System.Security.Cryptography;
using Clast.BloomFilter;

namespace PocketCsvReader.Indexing;

internal static class BloomIndexSidecar
{
    private static ReadOnlySpan<byte> Magic => "PKBLIDX1"u8;
    private const int DigestBytes = 32;
    private const int HeaderBytes = 64;

    internal static void Publish(FileStream source, SourceStamp stamp, int rowCount, byte[] compatibility,
        ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[HeaderBytes];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt64LittleEndian(header[8..], stamp.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..], stamp.LastWriteUtc.Ticks);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], rowCount);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], payload.Length);
        compatibility.CopyTo(header[32..]);
        var target = source.Name + ".bloom.idx";
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(header);
                file.Write(payload);
                file.Write(Checksum(header, payload));
            }

            if (SourceStamp.Read(source) != stamp || SourceStamp.Read(source.Name) != stamp)
                throw new IOException($"The file '{source.Name}' changed while building its Bloom index.");
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static ProbeResult? Probe(string path, BloomIndex index, Lazy<byte[]> compatibility,
        Lazy<ulong> requestedHash)
    {
        using var file = Open(path, index.RequiresIndex);
        if (file is null)
            return null;

        try
        {
            Span<byte> header = stackalloc byte[HeaderBytes];
            file.ReadExactly(header);
            if (!header[..8].SequenceEqual(Magic))
                throw new InvalidDataException("Invalid or unsupported Bloom index format.");

            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header[28..]);
            var rowCount = BinaryPrimitives.ReadInt32LittleEndian(header[24..]);
            if (rowCount < 0 || payloadLength is < BloomIndex.BlockBytes or > BloomIndex.MaxPayloadBytes
                || payloadLength % BloomIndex.BlockBytes != 0 || file.Length != HeaderBytes + payloadLength + DigestBytes)
                throw Corrupt();

            var payload = new byte[payloadLength];
            file.ReadExactly(payload);
            Span<byte> checksum = stackalloc byte[DigestBytes];
            file.ReadExactly(checksum);
            if (!checksum.SequenceEqual(Checksum(header, payload)))
                throw Corrupt();

            if (!header[32..].SequenceEqual(compatibility.Value))
                throw new InvalidDataException("The Bloom index is incompatible with the selected key, hash, or reader configuration.");

            // A negative result never needs to open the source. Validate the path
            // after reading the sidecar so a change during that read is detected.
            var mightContain = new SplitBlockBloomFilter(payload).MightContainHash(requestedHash.Value);
            var stamp = SourceStamp.Read(path);
            if (stamp.Length != BinaryPrimitives.ReadInt64LittleEndian(header[8..])
                || stamp.LastWriteUtc.Ticks != BinaryPrimitives.ReadInt64LittleEndian(header[16..]))
                throw new InvalidDataException("The Bloom index is stale.");
            return new(mightContain, stamp, rowCount);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The Bloom index is truncated.", exception);
        }
    }

    private static FileStream? Open(string path, bool required)
    {
        try
        {
            return new(path + ".bloom.idx", FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (FileNotFoundException) when (!required)
        {
            return null;
        }
    }

    private static byte[] Checksum(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(header);
        hash.AppendData(payload);
        return hash.GetHashAndReset();
    }

    private static InvalidDataException Corrupt() => new("The Bloom index is corrupt.");

    internal readonly record struct ProbeResult(bool MightContain, SourceStamp Stamp, int RowCount);
}
