using System.Security.Cryptography;

namespace PocketCsvReader.Indexing;

internal static class ZoneMapIndexSidecar
{
    private static ReadOnlySpan<byte> Magic => "PKZMIDX1"u8;
    private const int DigestBytes = 32;
    private const int BlockHeaderBytes = sizeof(long) + sizeof(int) + sizeof(byte);

    internal static void Publish(FileStream source, SourceStamp stamp, ZoneMapIndex index, byte[] compatibility,
        int rowCount, int fieldCount, IReadOnlyList<Block> blocks)
    {
        var target = source.Name + ".zonemap.idx";
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new BinaryWriter(file))
            {
                file.Position = DigestBytes;
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
                        WriteKey(writer, min);
                        WriteKey(writer, max);
                    }
                    else
                    {
                        writer.Write(false);
                    }
                }

                writer.Flush();
                file.Position = DigestBytes;
                var checksum = SHA256.HashData(file);
                file.Position = 0;
                writer.Write(checksum);
            }

            if (SourceStamp.Read(source) != stamp || SourceStamp.Read(source.Name) != stamp)
                throw new IOException($"The file '{source.Name}' changed while building its zone map.");
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static ReadPlan? Find(FileStream source, int bomLength, ZoneMapIndex index, Lazy<byte[]> compatibility,
        ZoneMapPredicate predicate)
    {
        using var file = Open(source.Name, index.RequiresIndex);
        if (file is null)
            return null;
        using var reader = new BinaryReader(file);
        try
        {
            Span<byte> checksum = stackalloc byte[DigestBytes];
            file.ReadExactly(checksum);
            if (!checksum.SequenceEqual(SHA256.HashData(file)))
                throw Corrupt();

            file.Position = DigestBytes;
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
                throw new InvalidDataException("Invalid or unsupported zone map format.");

            long sourceLength = reader.ReadInt64();
            long lastWrite = reader.ReadInt64();
            var blockSize = reader.ReadInt32();
            var rowCount = reader.ReadInt32();
            var fieldCount = reader.ReadInt32();
            if (blockSize <= 0 || rowCount < 0 || fieldCount < 0)
                throw Corrupt();

            if (blockSize != index.BlockSize || !reader.ReadBytes(DigestBytes).AsSpan().SequenceEqual(compatibility.Value))
                throw new InvalidDataException("The zone map is incompatible with the selected key, block size, or reader configuration.");

            var blocks = ReadBlocks(reader, bomLength, sourceLength, blockSize, rowCount, predicate);
            var stamp = SourceStamp.Read(source);
            if (stamp.Length != sourceLength || stamp.LastWriteUtc.Ticks != lastWrite || SourceStamp.Read(source.Name) != stamp)
                throw new InvalidDataException("The zone map is stale.");
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
            AddCandidate(candidates, new(offset, skippedRows, skippedDataRows, count));
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
        var min = ReadKey(reader, key.KeyType);
        var max = ReadKey(reader, key.KeyType);
        if (key.Compare(min, max) > 0)
            throw Corrupt();
        return predicate.Intersects(min, max);
    }

    private static void AddCandidate(List<CsvRecordBlock> candidates, CsvRecordBlock block)
    {
        // Keep the same parser and its read-ahead when consecutive blocks qualify.
        if (candidates.Count > 0 && candidates[^1] is var previous
            && previous.SkippedDataRows + previous.RowCount == block.SkippedDataRows)
            candidates[^1] = previous with { RowCount = previous.RowCount + block.RowCount };
        else
            candidates.Add(block);
    }

    private static FileStream? Open(string path, bool required)
    {
        try
        {
            return new(path + ".zonemap.idx", FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (FileNotFoundException) when (!required)
        {
            return null;
        }
    }

    private static void WriteKey(BinaryWriter writer, object value)
    {
        switch (value)
        {
            case string text:
                // Preserve the comparer input exactly, including surrogate code units
                // that a registered parser may return.
                writer.Write(text.Length);
                foreach (var character in text)
                    writer.Write((ushort)character);
                break;
            case DateOnly date: writer.Write(date.DayNumber); break;
            case sbyte number: writer.Write(number); break;
            case byte number: writer.Write(number); break;
            case short number: writer.Write(number); break;
            case ushort number: writer.Write(number); break;
            case int number: writer.Write(number); break;
            case uint number: writer.Write(number); break;
            case long number: writer.Write(number); break;
            case ulong number: writer.Write(number); break;
            case decimal number: writer.Write(number); break;
            default: throw new NotSupportedException($"Zone map bounds for '{value.GetType()}' are not supported.");
        }
    }

    private static object ReadKey(BinaryReader reader, Type type)
    {
        try
        {
            if (type == typeof(DateOnly))
                return DateOnly.FromDayNumber(reader.ReadInt32());
            return Type.GetTypeCode(type) switch
            {
                TypeCode.String => ReadText(reader),
                TypeCode.SByte => reader.ReadSByte(),
                TypeCode.Byte => reader.ReadByte(),
                TypeCode.Int16 => reader.ReadInt16(),
                TypeCode.UInt16 => reader.ReadUInt16(),
                TypeCode.Int32 => reader.ReadInt32(),
                TypeCode.UInt32 => reader.ReadUInt32(),
                TypeCode.Int64 => reader.ReadInt64(),
                TypeCode.UInt64 => reader.ReadUInt64(),
                TypeCode.Decimal => reader.ReadDecimal(),
                _ => throw new NotSupportedException($"Zone map bounds for '{type}' are not supported.")
            };
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The zone map contains an invalid bound.", exception);
        }
    }

    private static string ReadText(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var file = reader.BaseStream;
        if (length < 0 || length > (file.Length - file.Position) / sizeof(ushort))
            throw Corrupt();
        return string.Create(length, reader, static (text, input) =>
        {
            for (int i = 0; i < text.Length; i++)
                text[i] = (char)input.ReadUInt16();
        });
    }

    private static InvalidDataException Corrupt() => new("The zone map is corrupt.");

    internal readonly record struct Block(long Offset, int SkippedRows, object? Min, object? Max);
    internal readonly record struct ReadPlan(IReadOnlyList<CsvRecordBlock> Blocks, int RowCount, int FieldCount);
}
