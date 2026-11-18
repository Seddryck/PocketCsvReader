namespace PocketCsvReader;

internal static class CsvHeaders
{
    // Preserve the existing virtual header hooks for derived character parsers.
    public static string[][] Read(IRecordSource<CsvProfile> source)
        => source is RecordParser parser ? parser.ReadHeaders() : ReadCore(source);

    public static ValueTask<string[][]> ReadAsync(IRecordSource<CsvProfile> source, CancellationToken cancellationToken)
        => source is RecordParser parser ? parser.ReadHeadersAsync(cancellationToken) : ReadCoreAsync(source, cancellationToken);

    public static string[] GetFieldNames(string?[][] headers, DialectDescriptor dialect)
    {
        var names = new string[headers.Max(header => header.Length)];
        if (dialect.Header)
        {
            foreach (var header in headers)
            {
                string? last = null;
                for (var i = 0; i < names.Length; i++)
                {
                    if (i < header.Length)
                    {
                        var cell = header[i];
                        if (!string.IsNullOrEmpty(cell))
                            last = cell;
                        else if (headers.Length > 1)
                            continue;
                    }

                    // A short row still extends its last label into omitted columns.
                    if (last is not null)
                        names[i] = names[i] is { } name ? string.Concat(name, dialect.HeaderJoin, last) : last;
                }
            }
        }

        for (var i = 0; i < names.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(names[i]))
                names[i] = $"field_{(dialect.Header ? i + 1 : i)}";
        }

        return names;
    }

    internal static string[][] ReadCore(IRecordSource<CsvProfile> source)
    {
        var headers = new HeaderRows(source.Profile.Dialect);
        while (headers.NeedsRow)
        {
            source.IsEndOfFile(out var record, out _);
            headers.Add(record.AsMemory());
            PositionTracker.ExcludeRecord(source);
        }
        return headers.ToArray();
    }

    internal static async ValueTask<string[][]> ReadCoreAsync(IRecordSource<CsvProfile> source, CancellationToken cancellationToken)
    {
        var headers = new HeaderRows(source.Profile.Dialect);
        while (headers.NeedsRow)
        {
            var result = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            headers.Add(result.Record);
            PositionTracker.ExcludeRecord(source);
        }
        return headers.ToArray();
    }

    private sealed class HeaderRows(DialectDescriptor dialect)
    {
        private readonly int _lastRow = dialect.Header ? dialect.HeaderRows.Max() : 0;
        private readonly List<string[]> _headers = new();
        private int _row;
        public bool NeedsRow => _row < _lastRow;

        public void Add(RecordMemory record)
        {
            if (!dialect.HeaderRows.Contains(++_row)) return;
            var values = new string[record.ParsedFields?.Length ?? 0];
            for (int i = 0; i < values.Length; i++)
                values[i] = record.Slice(i).ToString();
            _headers.Add(values);
        }

        public string[][] ToArray() => [.. _headers];
    }
}
