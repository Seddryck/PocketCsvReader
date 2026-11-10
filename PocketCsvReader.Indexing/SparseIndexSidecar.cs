using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PocketCsvReader.Indexing;

internal static class SparseIndexSidecar
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly CsvProfile SidecarProfile = new(new DialectDescriptor(Header: false, LineTerminator: "\n"))
    {
        StreamInitialization = StreamInitializationOptions.Utf8
    };

    public static FileStream OpenSource(string path, int bufferSize, bool building = false)
        => new(path, FileMode.Open, FileAccess.Read, building ? FileShare.ReadWrite : FileShare.Read, bufferSize, useAsync: true);

    public static bool Supports(CsvProfile source)
        => Utf8RecordSource.Supports(source) && string.IsNullOrEmpty(source.Resource?.Compression)
            && (string.IsNullOrEmpty(source.Resource?.Encoding) || Encoding.GetEncoding(source.Resource.Encoding).CodePage == Encoding.UTF8.CodePage)
            && source.Dialect.CommentRows is not { Length: > 0 } && !source.ParserOptimizations.RowCountAtStart
            && (!source.Dialect.Header || source.Dialect.HeaderRepeat);

    public static CsvProfile CreateProfile(CsvProfile source, bool building)
        => new(source.Dialect, building ? null : source.Schema, building ? null : source.Resource, building ? null : source.Parsers)
        {
            Filters = building ? [] : source.Filters,
            Skip = building ? 0 : source.Skip,
            Take = building ? null : source.Take,
            RowNumber = building ? null : source.RowNumber,
            StreamInitialization = new(ProbeEncoding: true, InputMode: CsvInputMode.Utf8Bytes),
            ParserOptimizations = source.ParserOptimizations with { TrackPosition = building }
        };
    public static void Build(string path, CsvProfile profile, SparseIndex index)
    {
        using var stream = OpenSource(path, profile.ParserOptimizations.BufferSize, building: true);
        var before = SourceStamp.Read(stream);
        Span<byte> prefix = stackalloc byte[3];
        var prefixLength = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        var bomLength = prefix[..prefixLength].StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        stream.Position = 0;

        var checkpoints = new List<Checkpoint>();
        int rowCount = 0, fieldCount;
        using (var reader = new CsvDataReader(stream, profile, leaveOpen: true))
        {
            int ordinal = -1;
            object? previous = null;
            while (reader.Read())
            {
                if (ordinal < 0)
                    ordinal = CsvIndexLookup.ResolveOrdinal(reader, index.Key);
                var field = reader.GetDecodedField(ordinal);
                if (!field.HasValue)
                    throw new InvalidDataException($"Data record {reader.RowCount} has no value for the indexed column.");
                var keyText = field.Value;
                var key = index.Key.Parse(keyText);
                if (previous is not null && index.Key.Compare(previous, key) > 0)
                    throw new InvalidDataException($"The sparse index key in '{path}' is out of ascending order at data record {rowCount + 1}.");

                if (rowCount % index.CheckpointInterval == 0)
                    checkpoints.Add(new(checked(reader.CurrentPosition!.Value.Offset + bomLength), keyText.ToString()));
                previous = key;
                rowCount = checked(rowCount + 1);
            }
            if (rowCount == 0 && profile.Dialect.Header)
                CsvIndexLookup.ResolveOrdinal(reader, index.Key);
            fieldCount = reader.SourceHeaders?.Length ?? 0;
        }

        RequireUnchanged(stream, before);
        var metadata = CreateMetadata(index, profile, before, rowCount, fieldCount);
        metadata.Add("checksum", Checksum(metadata, checkpoints));
        var target = stream.Name + ".sparse.idx";
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file, Utf8) { NewLine = "\n" })
            {
                writer.WriteLine("format,pocket-sparse-index,1");
                foreach (var (name, value) in metadata)
                    WriteRecord(writer, "metadata", name, value);
                foreach (var checkpoint in checkpoints)
                    WriteRecord(writer, "checkpoint", checkpoint.Offset.ToString(Invariant), checkpoint.Text);
            }

            RequireUnchanged(stream, before);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static CsvRecordRange Find(FileStream source, CsvProfile profile, SparseIndex index, object requested)
    {
        using var file = new FileStream(source.Name + ".sparse.idx", FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var reader = new CsvDataReader(file, SidecarProfile);
        if (!reader.Read() || reader.FieldCount != 3 || reader.GetString(0) != "format"
            || reader.GetString(1) != "pocket-sparse-index" || reader.GetString(2) != "1")
            throw new InvalidDataException("Invalid or unsupported sparse index format.");

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var checkpoints = new List<Checkpoint>();
        while (reader.Read())
        {
            if (reader.FieldCount != 3)
                throw new InvalidDataException("A sparse index record must have exactly three fields.");
            string tag = reader.GetString(0), name = reader.GetString(1), value = reader.GetString(2);
            if (tag == "metadata" && checkpoints.Count == 0 && metadata.TryAdd(name, value))
                continue;
            if (tag != "checkpoint" || !long.TryParse(name, NumberStyles.None, Invariant, out var offset))
                throw new InvalidDataException("Invalid sparse index record.");
            checkpoints.Add(new(offset, value));
        }

        var sourceStamp = SourceStamp.Read(source);
        if (!metadata.TryGetValue("sourceByteLength", out var length)
            || !metadata.TryGetValue("sourceLastWriteUtc", out var lastWrite)
            || !long.TryParse(length, NumberStyles.None, Invariant, out var storedLength)
            || !DateTime.TryParseExact(lastWrite, "O", Invariant, DateTimeStyles.RoundtripKind, out var storedWrite)
            || storedWrite.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("Invalid sparse index source metadata.");
        if (storedLength != sourceStamp.Length || storedWrite != sourceStamp.LastWriteUtc)
            throw new InvalidDataException("The sparse index is stale.");

        if (!metadata.Remove("checksum", out var checksum) || checksum != Checksum(metadata, checkpoints)
            || !metadata.TryGetValue("dataRowCount", out var count)
            || !int.TryParse(count, NumberStyles.None, Invariant, out var rowCount)
            || !metadata.TryGetValue("fieldCount", out var fields)
            || !int.TryParse(fields, NumberStyles.None, Invariant, out var fieldCount))
            throw new InvalidDataException("The sparse index is corrupt.");

        var expected = CreateMetadata(index, profile, sourceStamp, rowCount, fieldCount);
        // The on-disk interval controls the interpretation of checkpoints. A caller
        // can use an existing index even if its next build would use a different interval.
        if (!metadata.TryGetValue("checkpointInterval", out var intervalText)
            || !int.TryParse(intervalText, NumberStyles.None, Invariant, out var interval) || interval == 0)
            throw new InvalidDataException("Invalid sparse index checkpoint interval.");
        expected["checkpointInterval"] = intervalText;
        if (metadata.Count != expected.Count || expected.Any(pair => !metadata.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new InvalidDataException("The sparse index is incompatible with the selected key or CSV configuration.");

        if (checkpoints.Count != (rowCount == 0 ? 0 : ((rowCount - 1) / interval) + 1))
            throw new InvalidDataException("The sparse index has the wrong number of checkpoints.");

        var keys = new object[checkpoints.Count];
        long previousOffset = -1;
        for (int i = 0; i < checkpoints.Count; i++)
        {
            var checkpoint = checkpoints[i];
            if (checkpoint.Offset <= previousOffset || checkpoint.Offset >= sourceStamp.Length)
                throw new InvalidDataException("Invalid sparse index checkpoint offset.");
            keys[i] = index.Key.Parse(checkpoint.Text);
            if (i > 0 && index.Key.Compare(keys[i - 1], keys[i]) > 0)
                throw new InvalidDataException("Sparse index checkpoints are out of order.");
            previousOffset = checkpoint.Offset;
        }

        int low = 0, high = keys.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (index.Key.Compare(keys[middle], requested) < 0)
                low = middle + 1;
            else
                high = middle;
        }

        int start = low - 1;
        return new(start < 0 ? null : checkpoints[start].Offset,
            start < 0 ? 0 : checked(start * interval), rowCount, fieldCount);
    }

    private static void RequireUnchanged(FileStream source, SourceStamp before)
    {
        if (SourceStamp.Read(source) != before)
            throw new IOException($"The CSV '{source.Name}' changed while building its sparse index.");
    }

    private static Dictionary<string, string> CreateMetadata(SparseIndex index, CsvProfile profile, SourceStamp stamp,
        int rowCount, int fieldCount)
    {
        var dialect = profile.Dialect;
        var configuration = Fingerprint([
            dialect.Header.ToString(), string.Join(",", dialect.HeaderRows.Select(row => row.ToString(Invariant))),
            dialect.HeaderJoin, dialect.HeaderRepeat.ToString(), dialect.CommentChar?.ToString(),
            dialect.Delimiter.ToString(), dialect.LineTerminator, dialect.QuoteChar?.ToString(),
            dialect.DoubleQuote.ToString(), dialect.EscapeChar?.ToString(), dialect.SkipInitialSpace.ToString()
        ]);
        return new(StringComparer.Ordinal)
        {
            [index.Key.ColumnName is null ? "columnOrdinal" : "columnName"] =
                index.Key.ColumnName ?? index.Key.ColumnOrdinal!.Value.ToString(Invariant),
            ["keyType"] = index.Key.KeyType.FullName!,
            ["checkpointInterval"] = index.CheckpointInterval.ToString(Invariant),
            ["sourceByteLength"] = stamp.Length.ToString(Invariant),
            ["sourceLastWriteUtc"] = stamp.LastWriteUtc.ToString("O", Invariant),
            ["csvConfigurationId"] = configuration,
            ["compatibilityId"] = index.Key.CompatibilityId,
            ["dataRowCount"] = rowCount.ToString(Invariant),
            ["fieldCount"] = fieldCount.ToString(Invariant)
        };
    }

    private static string Checksum(Dictionary<string, string> metadata, List<Checkpoint> checkpoints)
        => Fingerprint(metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => new[] { pair.Key, pair.Value })
            .Concat(checkpoints.SelectMany(checkpoint => new[] { checkpoint.Offset.ToString(Invariant), checkpoint.Text })));

    internal static string Fingerprint(IEnumerable<string?> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var value in values)
        {
            byte[]? bytes = value is null ? null : Utf8.GetBytes(value);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes?.Length ?? -1);
            hash.AppendData(length);
            if (bytes is not null)
                hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void WriteRecord(StreamWriter writer, string tag, string name, string value)
    {
        writer.Write(tag);
        writer.Write(',');
        writer.Write(name);
        writer.Write(',');
        if (value.AsSpan().IndexOfAny(",\"\r\n") < 0)
        {
            writer.WriteLine(value);
        }
        else
        {
            writer.Write('"');
            writer.Write(value.Replace("\"", "\"\""));
            writer.WriteLine('"');
        }
    }

    private readonly record struct SourceStamp(long Length, DateTime LastWriteUtc)
    {
        public static SourceStamp Read(FileStream source)
            => new(source.Length, File.GetLastWriteTimeUtc(source.SafeFileHandle));
    }

    private readonly record struct Checkpoint(long Offset, string Text);
}
