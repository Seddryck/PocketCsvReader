namespace PocketCsvReader.Indexing;

internal static class ZoneMapIndexSidecar
{
    private static ReadOnlySpan<byte> Magic => "PKZMIDX1"u8;
    private const string Suffix = ".zonemap.idx";
    private const string IndexName = "zone map";
    private const int BlockHeaderBytes = sizeof(long) + sizeof(int) + sizeof(byte);

    internal static void Publish(FileStream source, SourceStamp stamp, ZoneMapIndex index, byte[] compatibility,
        int rowCount, int fieldCount, IReadOnlyList<Block> blocks)
        => SeekIndexFile.Publish(source, stamp, Suffix, IndexName, writer =>
        {
            writer.Write(Magic);
            writer.Write(stamp.Length);
            writer.Write(stamp.LastWriteUtc.Ticks);
            writer.Write(index.BlockSize);
            writer.Write(rowCount);
            writer.Write(fieldCount);
            writer.Write(compatibility);
            foreach (var block in blocks)
            {
                writer.Write(block.Offset);
                writer.Write(block.SkippedRows);
                if (block is { Min: { } min, Max: { } max })
                {
                    writer.Write(true);
                    IndexKeyBinary.Write(writer, min);
                    IndexKeyBinary.Write(writer, max);
                }
                else
                {
                    writer.Write(false);
                }
            }
        });

    internal static CsvIndexedReadPlan? Find(FileStream source, int bomLength, ZoneMapIndex index, Lazy<byte[]> compatibility,
        ZoneMapPredicate predicate)
    {
        using var file = SeekIndexFile.Open(source.Name, Suffix, index.RequiresIndex);
        if (file is null)
            return null;
        using var reader = new BinaryReader(file);
        try
        {
            SeekIndexFile.ValidateChecksum(file, IndexName);
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
                throw new InvalidDataException("Invalid or unsupported zone map format.");

            long sourceLength = reader.ReadInt64();
            long lastWrite = reader.ReadInt64();
            var blockSize = reader.ReadInt32();
            var rowCount = reader.ReadInt32();
            var fieldCount = reader.ReadInt32();
            if (blockSize <= 0 || rowCount < 0 || fieldCount < 0)
                throw Corrupt();

            if (blockSize != index.BlockSize || !reader.ReadBytes(SeekIndexFile.DigestBytes).AsSpan().SequenceEqual(compatibility.Value))
                throw new InvalidDataException("The zone map is incompatible with the selected key, block size, or reader configuration.");

            var blocks = ReadBlocks(reader, bomLength, sourceLength, blockSize, rowCount, predicate);
            SeekIndexFile.ValidateSource(source, sourceLength, lastWrite, IndexName);
            return new(blocks, rowCount, fieldCount);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The zone map is truncated.", exception);
        }
    }

    private static List<CsvRecordBlock> ReadBlocks(BinaryReader reader, int bomLength, long sourceLength, int blockSize,
        int rowCount, ZoneMapPredicate predicate)
    {
        var file = reader.BaseStream;
        var blockCount = rowCount == 0 ? 0 : ((rowCount - 1) / blockSize) + 1;
        if (blockCount > (file.Length - file.Position) / BlockHeaderBytes)
            throw Corrupt();

        var candidates = new List<CsvRecordBlock>();
        long previousOffset = bomLength - 1L;
        long minimumSkippedRows = 0;
        for (var i = 0; i < blockCount; i++)
        {
            var offset = reader.ReadInt64();
            var skippedRows = reader.ReadInt32();
            if (offset <= previousOffset || offset >= sourceLength || skippedRows < minimumSkippedRows)
                throw Corrupt();
            previousOffset = offset;
            minimumSkippedRows = (long)skippedRows + blockSize;

            if (!ReadBoundsOverlap(reader, predicate))
                continue;

            var skippedDataRows = i * blockSize;
            var count = Math.Min(blockSize, rowCount - skippedDataRows);
            CsvIndexedReadPlan.AddCandidate(candidates, new(offset, skippedRows, skippedDataRows, count));
        }
        if (file.Position != file.Length)
            throw Corrupt();
        return candidates;
    }

    private static bool ReadBoundsOverlap(BinaryReader reader, ZoneMapPredicate predicate)
    {
        var boundsFlag = reader.ReadByte();
        if (boundsFlag == 0)
            return true;
        if (boundsFlag != 1)
            throw Corrupt();

        var key = predicate.Key;
        var min = IndexKeyBinary.Read(reader, key.KeyType);
        var max = IndexKeyBinary.Read(reader, key.KeyType);
        if (key.Compare(min, max) > 0)
            throw Corrupt();
        return predicate.Intersects(min, max);
    }

    private static InvalidDataException Corrupt() => new("The zone map is corrupt.");

    internal readonly record struct Block(long Offset, int SkippedRows, object? Min, object? Max);
}
