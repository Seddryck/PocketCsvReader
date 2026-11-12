using System.Numerics;

namespace PocketCsvReader.Indexing;

internal readonly record struct BitmapWord(int Index, ulong Bits);

internal sealed class BitmapColumn(IIndexKey key)
{
    internal SortedDictionary<object, List<BitmapWord>> Categories { get; } = new(Comparer<object>.Create(key.Compare));

    internal void Add(NullableSpan field, int row)
    {
        if (!field.HasValue || key.Parse(field.Value) is not { } value)
            return;
        if (!Categories.TryGetValue(value, out var words))
        {
            words = [];
            Categories.Add(value, words);
        }

        int index = row / 64;
        ulong bit = 1UL << (row % 64);
        if (words.Count > 0 && words[^1].Index == index)
            words[^1] = new(index, words[^1].Bits | bit);
        else
            words.Add(new(index, bit));
    }
}

internal static class BitmapIndexSidecar
{
    private static ReadOnlySpan<byte> Magic => "PKBMIDX1"u8;
    private const string Suffix = ".bitmap.idx";
    private const string IndexName = "bitmap index";
    private const int LocationBytes = sizeof(long) + sizeof(int);
    private const int WordBytes = sizeof(int) + sizeof(ulong);

    internal static void Publish(FileStream source, SourceStamp stamp, byte[] compatibility, int fieldCount,
        IReadOnlyList<RowLocation> locations, IReadOnlyList<BitmapColumn> columns)
        => SeekIndexFile.Publish(source, stamp, Suffix, IndexName, writer =>
        {
            writer.Write(Magic);
            writer.Write(stamp.Length);
            writer.Write(stamp.LastWriteUtc.Ticks);
            writer.Write(locations.Count);
            writer.Write(fieldCount);
            writer.Write(columns.Count);
            writer.Write(compatibility);
            foreach (var location in locations)
            {
                writer.Write(location.Offset);
                writer.Write(location.SkippedRows);
            }
            foreach (var column in columns)
                WriteColumn(writer, column);
        });

    internal static CsvIndexedReadPlan? Find(FileStream source, int bomLength, bool required,
        IReadOnlyList<IIndexKey> keys, BitmapLookup lookup, Lazy<byte[]> compatibility)
    {
        using var file = SeekIndexFile.Open(source.Name, Suffix, required);
        if (file is null)
            return null;

        using var reader = new BinaryReader(file);
        try
        {
            SeekIndexFile.ValidateChecksum(file, IndexName);
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
                throw new InvalidDataException("Invalid or unsupported bitmap index format.");

            long sourceLength = reader.ReadInt64();
            long lastWrite = reader.ReadInt64();
            int rowCount = reader.ReadInt32();
            int fieldCount = reader.ReadInt32();
            int columns = reader.ReadInt32();
            if (rowCount < 0 || fieldCount < 0 || columns <= 0)
                throw Corrupt();
            if (columns != keys.Count || !reader.ReadBytes(SeekIndexFile.DigestBytes).AsSpan().SequenceEqual(compatibility.Value))
                throw new InvalidDataException("The bitmap index is incompatible with the selected keys or reader configuration.");

            var locationsStart = file.Position;
            var bitmapStart = locationsStart + ((long)rowCount * LocationBytes);
            if (bitmapStart > file.Length - ((long)columns * sizeof(int)))
                throw Corrupt();

            file.Position = bitmapStart;
            var selected = ReadSelection(reader, rowCount, keys, lookup);
            var blocks = ReadCandidates(reader, locationsStart, selected, bomLength, sourceLength);
            SeekIndexFile.ValidateSource(source, sourceLength, lastWrite, IndexName);
            return new(blocks, rowCount, fieldCount);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The bitmap index is truncated.", exception);
        }
    }

    private static void WriteColumn(BinaryWriter writer, BitmapColumn column)
    {
        writer.Write(column.Categories.Count);
        foreach (var (key, words) in column.Categories)
        {
            IndexKeyBinary.Write(writer, key);
            writer.Write(words.Count);
            foreach (var word in words)
            {
                writer.Write(word.Index);
                writer.Write(word.Bits);
            }
        }
    }

    private static ulong[] ReadSelection(BinaryReader reader, int rowCount, IReadOnlyList<IIndexKey> keys, BitmapLookup lookup)
    {
        var wordCount = (int)((rowCount + 63L) / 64);
        var selected = new ulong[wordCount];
        var alternatives = new ulong[wordCount];
        Array.Fill(selected, ulong.MaxValue);
        for (int column = 0; column < keys.Count; column++)
        {
            var values = lookup.Values[column];
            ReadColumn(reader, keys[column], values, rowCount, alternatives);
            if (values is null)
                continue;

            for (int word = 0; word < selected.Length; word++)
                selected[word] &= alternatives[word];
            Array.Clear(alternatives);
        }

        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw Corrupt();
        return selected;
    }

    private static void ReadColumn(BinaryReader reader, IIndexKey key, SortedSet<object>? requested, int rowCount,
        ulong[] alternatives)
    {
        int categoryCount = reader.ReadInt32();
        if (categoryCount < 0 || categoryCount > rowCount)
            throw Corrupt();

        object? previous = null;
        for (int category = 0; category < categoryCount; category++)
        {
            var value = IndexKeyBinary.Read(reader, key.KeyType);
            if (previous is not null && key.Compare(previous, value) >= 0)
                throw Corrupt();
            previous = value;
            ReadWords(reader, alternatives, rowCount, requested?.Contains(value) == true);
        }
    }

    private static void ReadWords(BinaryReader reader, ulong[] alternatives, int rowCount, bool matches)
    {
        int count = reader.ReadInt32();
        var file = reader.BaseStream;
        if (count <= 0 || count > alternatives.Length || count > (file.Length - file.Position) / WordBytes)
            throw Corrupt();

        int previous = -1;
        int tailBits = rowCount % 64;
        for (int i = 0; i < count; i++)
        {
            int index = reader.ReadInt32();
            ulong bits = reader.ReadUInt64();
            if (index <= previous || index >= alternatives.Length || bits == 0)
                throw Corrupt();
            if (index == alternatives.Length - 1 && tailBits != 0 && (bits >> tailBits) != 0)
                throw Corrupt();
            if (matches)
                alternatives[index] |= bits;
            previous = index;
        }
    }

    private static List<CsvRecordBlock> ReadCandidates(BinaryReader reader, long locationsStart, ulong[] selected,
        int bomLength, long sourceLength)
    {
        var candidates = new List<CsvRecordBlock>();
        long previousOffset = bomLength - 1L;
        int previousRow = -2, previousComments = 0;
        foreach (int row in EnumerateRows(selected))
        {
            if (row != previousRow + 1)
                reader.BaseStream.Position = locationsStart + ((long)row * LocationBytes);

            long offset = reader.ReadInt64();
            int skippedRows = reader.ReadInt32();
            if (offset <= previousOffset || offset >= sourceLength || skippedRows < row || skippedRows - row < previousComments)
                throw Corrupt();

            CsvIndexedReadPlan.AddCandidate(candidates, new(offset, skippedRows, row, 1));
            previousOffset = offset;
            previousRow = row;
            previousComments = skippedRows - row;
        }
        return candidates;
    }

    private static IEnumerable<int> EnumerateRows(ulong[] selected)
    {
        for (int i = 0; i < selected.Length; i++)
        {
            ulong bits = selected[i];
            while (bits != 0)
            {
                yield return (i * 64) + BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
            }
        }
    }

    private static InvalidDataException Corrupt() => new("The bitmap index is corrupt.");

    internal readonly record struct RowLocation(long Offset, int SkippedRows);
}
