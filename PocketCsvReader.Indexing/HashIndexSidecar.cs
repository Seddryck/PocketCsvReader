namespace PocketCsvReader.Indexing;

internal static class HashIndexSidecar
{
    private static ReadOnlySpan<byte> Magic => "PKHSIDX1"u8;
    private const string Suffix = ".hash.idx";
    private const string IndexName = "hash index";
    private const int EntryBytes = sizeof(ulong) + sizeof(long) + (2 * sizeof(int));

    internal static void Publish(FileStream source, SourceStamp stamp, byte[] compatibility,
        int rowCount, int fieldCount, List<Entry> entries)
    {
        entries.Sort(static (left, right) =>
        {
            var order = left.Hash.CompareTo(right.Hash);
            return order == 0 ? left.Offset.CompareTo(right.Offset) : order;
        });

        SeekIndexFile.Publish(source, stamp, Suffix, IndexName, writer =>
        {
            writer.Write(Magic);
            writer.Write(stamp.Length);
            writer.Write(stamp.LastWriteUtc.Ticks);
            writer.Write(rowCount);
            writer.Write(fieldCount);
            writer.Write(entries.Count);
            writer.Write(compatibility);
            foreach (var entry in entries)
            {
                writer.Write(entry.Hash);
                writer.Write(entry.Offset);
                writer.Write(entry.SkippedRows);
                writer.Write(entry.SkippedDataRows);
            }
        });
    }

    internal static CsvIndexedReadPlan? Find(FileStream source, int bomLength, HashIndex index,
        Lazy<byte[]> compatibility, Lazy<ulong> requestedHash)
    {
        using var file = SeekIndexFile.Open(source.Name, Suffix, index.RequiresIndex);
        if (file is null)
            return null;

        using var reader = new BinaryReader(file);
        try
        {
            SeekIndexFile.ValidateChecksum(file, IndexName);
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
                throw new InvalidDataException("Invalid or unsupported hash index format.");

            long sourceLength = reader.ReadInt64();
            long lastWrite = reader.ReadInt64();
            int rowCount = reader.ReadInt32();
            int fieldCount = reader.ReadInt32();
            int entryCount = reader.ReadInt32();
            if (rowCount < 0 || fieldCount < 0 || entryCount < 0 || entryCount > rowCount
                || file.Length - file.Position != SeekIndexFile.DigestBytes + ((long)entryCount * EntryBytes))
                throw Corrupt();

            if (!reader.ReadBytes(SeekIndexFile.DigestBytes).AsSpan().SequenceEqual(compatibility.Value))
                throw new InvalidDataException("The hash index is incompatible with the selected key, hash, or reader configuration.");

            var candidates = ReadCandidates(reader, entryCount, bomLength, sourceLength, rowCount, requestedHash.Value);
            SeekIndexFile.ValidateSource(source, sourceLength, lastWrite, IndexName);
            return new(candidates, rowCount, fieldCount);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The hash index is truncated.", exception);
        }
    }

    private static List<CsvRecordBlock> ReadCandidates(BinaryReader reader, int entryCount, int bomLength,
        long sourceLength, int rowCount, ulong requested)
    {
        var file = reader.BaseStream;
        var start = file.Position;
        int low = 0, high = entryCount;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            file.Position = start + ((long)middle * EntryBytes);
            if (reader.ReadUInt64() < requested)
                low = middle + 1;
            else
                high = middle;
        }

        // Equal hashes are sorted by source offset, so duplicates and collisions
        // can be visited in source order without another sort or a source scan.
        file.Position = start + ((long)low * EntryBytes);
        var candidates = new List<CsvRecordBlock>();
        long previousOffset = bomLength - 1L;
        int previousDataRow = -1;
        int previousCommentCount = 0;
        for (int i = low; i < entryCount && reader.ReadUInt64() == requested; i++)
        {
            long offset = reader.ReadInt64();
            int skippedRows = reader.ReadInt32();
            int skippedDataRows = reader.ReadInt32();
            if (offset <= previousOffset || offset >= sourceLength || skippedDataRows <= previousDataRow
                || skippedDataRows >= rowCount || skippedRows < skippedDataRows
                || skippedRows - skippedDataRows < previousCommentCount)
                throw Corrupt();

            previousOffset = offset;
            previousDataRow = skippedDataRows;
            previousCommentCount = skippedRows - skippedDataRows;
            CsvIndexedReadPlan.AddCandidate(candidates, new(offset, skippedRows, skippedDataRows, 1));
        }
        return candidates;
    }

    private static InvalidDataException Corrupt() => new("The hash index is corrupt.");

    internal readonly record struct Entry(ulong Hash, long Offset, int SkippedRows, int SkippedDataRows);
}
