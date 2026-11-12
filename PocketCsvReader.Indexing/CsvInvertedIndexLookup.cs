using System.Data.Common;

namespace PocketCsvReader.Indexing;

/// <summary>Builds inverted indexes and applies term predicates to CSV sources.</summary>
public static class CsvInvertedIndexLookup
{
    private const string IndexName = "Inverted indexes";

    /// <summary>Builds or replaces the inverted sidecar for one CSV file.</summary>
    public static void BuildIndexes(this CsvReader reader, string path, InvertedIndex index)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        if (reader.GetType() != typeof(CsvReader))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);
        CsvIndexLookup.ForFile(reader, path).BuildIndexes(index);
    }

    /// <summary>Builds or replaces the inverted sidecar for each selected CSV file.</summary>
    public static void BuildIndexes<TPartition>(this CsvFileQuery<TPartition> query, InvertedIndex index)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        if (!CsvSeekIndexLookup.Supports(query))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        var profile = query.Profile.ForIndexSeeking(trackPosition: true).WithFilters([]);
        var key = index.Key.ResolveRuntimeParser(query.Profile.Parsers);
        var compatibility = GetCompatibility(query.Profile, key, index.TermRules);
        foreach (var file in query.EnumerateFiles())
        {
            if (file.Opener is not null)
                throw CsvSeekIndexLookup.UnsupportedSource(IndexName);
            BuildFile(file.Path, profile, key, index.TermRules, compatibility);
        }
    }

    /// <summary>Finds records matching a term predicate in one CSV file.</summary>
    public static DbDataReader WithIndexedLookup(this CsvReader reader, string path, InvertedIndex index, TermPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(predicate);
        if (reader.GetType() == typeof(CsvReader))
            return CsvIndexLookup.ForFile(reader, path).WithIndexedLookup(index, predicate).ToDbDataReader();
        if (index.RequiresIndex)
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        var key = index.Key.ResolveRuntimeParser(reader.Profile.Parsers);
        var lookup = predicate.Bind(index);
        return new DbDataReaderAdapter(CsvSeekIndexLookup.Select(reader.ToDataReader(path), CreateSelector(key, lookup)));
    }

    /// <summary>Finds records matching a term predicate in the selected CSV files.</summary>
    public static CsvFileQuery<TPartition> WithIndexedLookup<TPartition>(this CsvFileQuery<TPartition> query,
        InvertedIndex index, TermPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(predicate);
        var profile = query.Profile;
        var key = index.Key.ResolveRuntimeParser(profile.Parsers);
        var lookup = predicate.Bind(index);
        var compatibility = new Lazy<byte[]>(() => GetCompatibility(profile, key, index.TermRules));
        return CsvSeekIndexLookup.WithLookup(query, CsvIndexKind.Inverted,
            indexed => indexed ? null : CreateSelector(key, lookup),
            (source, bomLength) => InvertedIndexSidecar.Find(source, bomLength, index, lookup, compatibility),
            index.RequiresIndex, IndexName);
    }

    private static Func<CsvDataReader, RecordSelection> CreateSelector(IIndexKey key, TermLookup lookup)
    {
        if (lookup.Terms.Count == 0)
            return static _ => RecordSelection.Skip;
        var matches = lookup.CreateMatcher();
        return CsvSeekIndexLookup.CreateSelector(key,
            field => field.HasValue && key.Parse(field.Value) is { } parsed && matches(parsed)
                ? RecordSelection.Include : RecordSelection.Skip);
    }

    private static void BuildFile(string path, CsvProfile profile, IIndexKey key, InvertedTermRules rules, byte[] compatibility)
    {
        using var source = SparseIndexSidecar.OpenSource(path, profile.ParserOptimizations.BufferSize, building: true);
        if (!source.CanSeek)
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);
        var stamp = SourceStamp.Read(source);
        if (!SparseIndexSidecar.TryReadUtf8BomLength(source, out var bomLength))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        using var reader = new CsvDataReader(source, profile, leaveOpen: true) { Window = null };
        var terms = new SortedDictionary<object, List<InvertedIndexSidecar.Posting>>(Comparer<object>.Create(rules.Key.Compare));
        int ordinal = -1, rowCount = 0;
        while (reader.Read())
        {
            if (ordinal < 0)
                ordinal = CsvIndexLookup.ResolveOrdinal(reader, key);
            var field = reader.GetIndexField(ordinal);
            if (field.HasValue && key.Parse(field.Value) is { } parsed)
                AddPostings(terms, rules.Extract(parsed), reader, bomLength, rowCount);
            rowCount = checked(rowCount + 1);
        }

        if (profile.Dialect.Header && ordinal < 0)
            CsvIndexLookup.ResolveOrdinal(reader, key);
        InvertedIndexSidecar.Publish(source, stamp, compatibility, rowCount, reader.SourceHeaders?.Length ?? 0, terms);
    }

    private static void AddPostings(SortedDictionary<object, List<InvertedIndexSidecar.Posting>> terms, IEnumerable<object> extracted,
        CsvDataReader reader, int bomLength, int dataRow)
    {
        InvertedIndexSidecar.Posting? location = null;
        foreach (var term in extracted)
        {
            if (!terms.TryGetValue(term, out var postings))
            {
                postings = [];
                terms.Add(term, postings);
            }
            if (postings.Count > 0 && postings[^1].DataRow == dataRow)
                continue;

            location ??= new(CsvSeekIndexLookup.GetRecordOffset(reader, bomLength), reader.RowCount - 1, dataRow);
            postings.Add(location.Value);
        }
    }

    private static byte[] GetCompatibility(CsvProfile profile, IIndexKey key, InvertedTermRules rules)
        => Convert.FromHexString(SparseIndexSidecar.Fingerprint([
            rules.CompatibilityId, .. CsvIndexLookup.GetKeyCompatibilitySettings(profile, key)
        ]));
}
