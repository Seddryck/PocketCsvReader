namespace PocketCsvReader.Indexing;

internal static class InvertedIndexSidecar
{
    private static ReadOnlySpan<byte> Magic => "PKIVIDX1"u8;
    private const string Suffix = ".inverted.idx";
    private const string IndexName = "inverted index";
    private const int PostingBytes = sizeof(long) + (2 * sizeof(int));

    internal static void Publish(FileStream source, SourceStamp stamp, byte[] compatibility, int rowCount, int fieldCount,
        SortedDictionary<object, List<Posting>> terms)
        => SeekIndexFile.Publish(source, stamp, Suffix, IndexName, writer =>
        {
            writer.Write(Magic);
            writer.Write(stamp.Length);
            writer.Write(stamp.LastWriteUtc.Ticks);
            writer.Write(rowCount);
            writer.Write(fieldCount);
            writer.Write(terms.Count);
            writer.Write(compatibility);
            foreach (var (term, postings) in terms)
            {
                IndexKeyBinary.Write(writer, term);
                writer.Write(postings.Count);
                foreach (var posting in postings)
                {
                    writer.Write(posting.Offset);
                    writer.Write(posting.SkippedRows);
                    writer.Write(posting.DataRow);
                }
            }
        });

    internal static CsvIndexedReadPlan? Find(FileStream source, int bomLength, InvertedIndex index,
        TermLookup lookup, Lazy<byte[]> compatibility)
    {
        using var file = SeekIndexFile.Open(source.Name, Suffix, index.RequiresIndex);
        if (file is null)
            return null;

        using var reader = new BinaryReader(file);
        try
        {
            SeekIndexFile.ValidateChecksum(file, IndexName);
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
                throw new InvalidDataException("Invalid or unsupported inverted index format.");

            long sourceLength = reader.ReadInt64();
            long lastWrite = reader.ReadInt64();
            int rowCount = reader.ReadInt32();
            int fieldCount = reader.ReadInt32();
            int termCount = reader.ReadInt32();
            if (rowCount < 0 || fieldCount < 0 || termCount < 0 || (rowCount == 0 && termCount != 0))
                throw Corrupt();
            if (!reader.ReadBytes(SeekIndexFile.DigestBytes).AsSpan().SequenceEqual(compatibility.Value))
                throw new InvalidDataException("The inverted index is incompatible with the key, term rules, or reader configuration.");

            var lists = FindTermLists(reader, index.TermRules.Key, lookup.Terms, termCount, rowCount);
            var rows = new PostingReader(reader, bomLength, sourceLength, rowCount);
            var candidates = rows.ToReadBlocks(Combine(lists, rows, lookup));
            SeekIndexFile.ValidateSource(source, sourceLength, lastWrite, IndexName);
            return new(candidates, rowCount, fieldCount);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The inverted index is truncated.", exception);
        }
    }

    private static List<PostingList> FindTermLists(BinaryReader reader, IIndexKey termKey, SortedSet<object> requested,
        int termCount, int rowCount)
    {
        var file = reader.BaseStream;
        var lists = new List<PostingList>();
        object? previous = null;
        for (int i = 0; i < termCount; i++)
        {
            var term = IndexKeyBinary.Read(reader, termKey.KeyType);
            if (previous is not null && termKey.Compare(previous, term) >= 0)
                throw Corrupt();
            previous = term;

            int count = reader.ReadInt32();
            if (count <= 0 || count > rowCount || count > (file.Length - file.Position) / PostingBytes)
                throw Corrupt();
            if (requested.Contains(term))
                lists.Add(new(file.Position, count));
            file.Position += (long)count * PostingBytes;
        }

        if (file.Position != file.Length)
            throw Corrupt();
        return lists;
    }

    private static IEnumerable<Posting> Combine(List<PostingList> lists, PostingReader reader, TermLookup lookup)
    {
        if (lists.Count == 0 || (lookup.RequiresAll && lists.Count != lookup.Terms.Count))
            return [];
        if (lists.Count == 1)
            return reader.Read(lists[0]);

        // An intersection only needs to keep track of rows from its smallest list.
        if (lookup.RequiresAll)
            lists.Sort(static (left, right) => left.Count.CompareTo(right.Count));

        var matches = new Dictionary<int, PostingMatch>();
        for (int i = 0; i < lists.Count; i++)
        {
            bool includeNew = !lookup.RequiresAll || i == 0;
            foreach (var posting in reader.Read(lists[i]))
                Accumulate(matches, posting, includeNew);
        }

        int required = lookup.RequiresAll ? lists.Count : 1;
        return matches.Values.Where(match => match.Count >= required)
            .Select(match => match.Location).OrderBy(location => location.DataRow);
    }

    private static void Accumulate(Dictionary<int, PostingMatch> matches, Posting posting, bool includeNew)
    {
        if (matches.TryGetValue(posting.DataRow, out var previous))
        {
            if (previous.Location != posting)
                throw Corrupt();
            matches[posting.DataRow] = previous with { Count = previous.Count + 1 };
        }
        else if (includeNew)
        {
            matches.Add(posting.DataRow, new(posting, 1));
        }
    }

    private static InvalidDataException Corrupt() => new("The inverted index is corrupt.");

    internal readonly record struct Posting(long Offset, int SkippedRows, int DataRow);

    private readonly record struct PostingList(long Offset, int Count);

    private readonly record struct PostingMatch(Posting Location, int Count);

    private sealed class PostingReader(BinaryReader reader, int bomLength, long sourceLength, int rowCount)
    {
        internal IEnumerable<Posting> Read(PostingList list)
        {
            reader.BaseStream.Position = list.Offset;
            var previous = new Posting(bomLength - 1L, -1, -1);
            for (int i = 0; i < list.Count; i++)
            {
                var posting = new Posting(reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt32());
                Validate(posting, previous);
                previous = posting;
                yield return posting;
            }
        }

        internal List<CsvRecordBlock> ToReadBlocks(IEnumerable<Posting> selected)
        {
            var candidates = new List<CsvRecordBlock>();
            var previous = new Posting(bomLength - 1L, -1, -1);
            foreach (var posting in selected)
            {
                Validate(posting, previous);
                CsvIndexedReadPlan.AddCandidate(candidates, new(posting.Offset, posting.SkippedRows, posting.DataRow, 1));
                previous = posting;
            }
            return candidates;
        }

        private void Validate(Posting posting, Posting previous)
        {
            if (posting.Offset <= previous.Offset || posting.Offset >= sourceLength
                || posting.DataRow <= previous.DataRow || posting.DataRow >= rowCount
                || posting.SkippedRows < posting.DataRow
                || posting.SkippedRows - posting.DataRow < previous.SkippedRows - previous.DataRow)
                throw Corrupt();
        }
    }
}
