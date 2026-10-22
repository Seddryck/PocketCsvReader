namespace PocketCsvReader;

internal static class CsvHeaders
{
    // Preserve the existing virtual header hooks for derived character parsers.
    public static string[][] Read(IRecordSource<CsvProfile> source)
        => source is RecordParser parser ? parser.ReadHeaders() : ReadCore(source);

    public static ValueTask<string[][]> ReadAsync(IRecordSource<CsvProfile> source, CancellationToken cancellationToken)
        => source is RecordParser parser ? parser.ReadHeadersAsync(cancellationToken) : ReadCoreAsync(source, cancellationToken);

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
