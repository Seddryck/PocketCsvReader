using System.Security.Cryptography;

namespace PocketCsvReader.Indexing;

internal static class SeekIndexFile
{
    internal const int DigestBytes = 32;

    internal static void Publish(FileStream source, SourceStamp stamp, string suffix, string name, Action<BinaryWriter> write)
    {
        var target = source.Name + suffix;
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new BinaryWriter(file))
            {
                file.Position = DigestBytes;
                write(writer);
                writer.Flush();
                file.Position = DigestBytes;
                var checksum = SHA256.HashData(file);
                file.Position = 0;
                writer.Write(checksum);
            }

            if (SourceStamp.Read(source) != stamp || SourceStamp.Read(source.Name) != stamp)
                throw new IOException($"The file '{source.Name}' changed while building its {name}.");
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static FileStream? Open(string path, string suffix, bool required)
    {
        try
        {
            return new(path + suffix, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (FileNotFoundException) when (!required)
        {
            return null;
        }
    }

    internal static void ValidateChecksum(FileStream file, string name)
    {
        Span<byte> checksum = stackalloc byte[DigestBytes];
        file.ReadExactly(checksum);
        if (!checksum.SequenceEqual(SHA256.HashData(file)))
            throw new InvalidDataException($"The {name} is corrupt.");
        file.Position = DigestBytes;
    }

    internal static void ValidateSource(FileStream source, long length, long lastWriteTicks, string name)
    {
        var stamp = SourceStamp.Read(source);
        if (stamp.Length != length || stamp.LastWriteUtc.Ticks != lastWriteTicks || SourceStamp.Read(source.Name) != stamp)
            throw new InvalidDataException($"The {name} is stale.");
    }
}
