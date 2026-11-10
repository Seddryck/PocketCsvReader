using System.Data;
using System.Data.Common;
using System.Text;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Indexing;

/// <summary>Builds and queries sparse indexes using a format reader and a filesystem path.</summary>
public static class FlatFileIndexLookup
{
    /// <summary>Builds or replaces a sparse sidecar for a file sorted by the index key.</summary>
    public static void BuildIndexes<TProfile, TDataReader>(this FlatFileReader<TProfile, TDataReader> reader,
        string path, SparseIndex index)
        where TProfile : IProfile
        where TDataReader : IDataReader
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        if (reader is CsvReader csv && reader.GetType() == typeof(CsvReader))
        {
            CsvIndexLookup.ForFile(csv, path).BuildIndexes(index);
            return;
        }

        var configuration = GetConfiguration(reader) ?? throw UnsupportedSource();
        index = index.ResolveRuntimeParser(reader.Profile.Parsers);
        SparseIndexSidecar.Build(path, reader.FileBufferSize, index, configuration,
            (source, bomLength) =>
            {
                source.Position = bomLength;
                var input = AsBaseReader<TProfile>(reader.ToDataReader(source));
                input.UseUtf8FileInput(trackByteOffsets: true, restarted: false);
                return input;
            },
            row => row.GetLookupField(index.Key.ColumnName, index.Key.ColumnOrdinal),
            _ => 0);
    }

    /// <summary>Looks up a key in a file using its sidecar or, for an optional index, a scan.</summary>
    public static DbDataReader WithIndexedLookup<TProfile, TDataReader, TKey>(
        this FlatFileReader<TProfile, TDataReader> reader, string path, SparseIndex index, TKey value)
        where TProfile : IProfile
        where TDataReader : IDataReader
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        var requested = ReaderLookup.ValidateKey(index.Key, value);
        if (reader is CsvReader csv && reader.GetType() == typeof(CsvReader))
            return CsvIndexLookup.ForFile(csv, path).WithIndexedLookup(index, value).ToDbDataReader();

        index = index.ResolveRuntimeParser(reader.Profile.Parsers);
        var key = index.Key;
        var configuration = GetConfiguration(reader);
        if (configuration is null)
        {
            if (index.RequiresIndex)
                throw UnsupportedSource();

            return Select<TProfile>(reader.ToDataReader(path), key, requested, ordered: false);
        }

        var source = SparseIndexSidecar.OpenSource(path, reader.FileBufferSize);
        try
        {
            var range = SparseIndexSidecar.Find(source, configuration.Value, index, requested);
            if (range is null)
                return Select<TProfile>(reader.ToDataReader(source), key, requested, ordered: false);

            var restart = range.Value.Offset;
            var seekAfterHeader = reader.ReadsHeaderBeforeSeek ? restart : null;
            source.Position = seekAfterHeader.HasValue || restart is null
                ? SparseIndexSidecar.ReadUtf8BomLength(source) : restart.Value;
            var input = AsBaseReader<TProfile>(reader.ToDataReader(source));
            input.UseUtf8FileInput(trackByteOffsets: false, restarted: restart.HasValue, seekAfterHeader);
            return Select<TProfile>(input, key, requested, ordered: true);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static DbDataReader Select<TProfile>(IDataReader reader, IIndexKey key, object value, bool ordered)
        where TProfile : IProfile
    {
        if (reader is BaseDataReader<TProfile> input)
        {
            input.SelectFileRecords(row =>
                ReaderLookup.Select(row.GetLookupField(key.ColumnName, key.ColumnOrdinal), key, value, ordered));
            return new DbDataReaderAdapter(input);
        }
        return ReaderLookup.Scan(reader, key, value);
    }

    private static BaseDataReader<TProfile> AsBaseReader<TProfile>(IDataReader reader)
        where TProfile : IProfile
    {
        if (reader is BaseDataReader<TProfile> input)
            return input;
        reader.Dispose();
        throw UnsupportedSource();
    }

    private static SourceConfiguration? GetConfiguration<TProfile, TDataReader>(FlatFileReader<TProfile, TDataReader> reader)
        where TProfile : IProfile
        where TDataReader : IDataReader
    {
        var profile = reader.Profile;
        if (!IsUncompressedUtf8(profile) || reader.SeekConfiguration is not { } settings)
            return null;

        var schema = profile.Schema;
        return new("sourceConfigurationId", SparseIndexSidecar.Fingerprint([
            SparseIndexSidecar.Fingerprint(settings),
            SparseIndexSidecar.Fingerprint(SequenceSettings(profile.Resource?.Sequences)),
            schema?.IsMatchingByName.ToString(),
            schema is null ? null : SparseIndexSidecar.Fingerprint(schema.Fields.Select(field =>
                SparseIndexSidecar.Fingerprint([field.Name, .. SequenceSettings(field.Sequences)])))
        ]));
    }

    private static IEnumerable<string?> SequenceSettings(ImmutableSequenceCollection? sequences)
    {
        yield return (sequences?.Count() ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (sequences is null)
            yield break;
        foreach (var (key, value) in sequences)
        {
            yield return key.ToString();
            yield return value?.ToString();
        }
    }

    internal static bool IsUncompressedUtf8(IProfile source)
        => string.IsNullOrEmpty(source.Resource?.Compression)
            && (string.IsNullOrEmpty(source.Resource?.Encoding) || Encoding.GetEncoding(source.Resource.Encoding).CodePage == Encoding.UTF8.CodePage);

    private static NotSupportedException UnsupportedSource()
        => new("This reader configuration cannot restart at sparse checkpoints. Use an optional index to allow scanning.");
}
