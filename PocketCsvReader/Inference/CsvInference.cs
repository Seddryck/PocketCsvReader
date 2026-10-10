using System.Globalization;
using System.Text;
using PocketCsvReader.Compression;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;

/// <summary>Infers a reusable schema and, optionally, a CSV dialect from a bounded sample.</summary>
public static class CsvInference
{
    private static readonly string[] DatePatterns = ["yyyy-MM-dd", "yyyyMMdd", "d"];
    private static readonly string[] TimePatterns = ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF", "t", "T"];
    private static readonly string[] DateTimePatterns =
        ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "g", "G"];
    private static readonly string[] DateTimeOffsetPatterns =
        ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ss zzz", "yyyy-MM-dd HH:mm:ss.FFFFFFF zzz"];

    public static CsvInferenceResult Infer(string filename, CsvInferenceOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        if (!File.Exists(filename))
            throw new FileNotFoundException($"The file {filename} was not found.", filename);

        options ??= new CsvInferenceOptions();
        var resource = options.Resource ?? InferResource(filename);
        using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        return InferCore(stream, options with { Resource = resource });
    }

    /// <summary>
    /// Infers from the stream's current position. Seekable streams are restored by default; a
    /// non-seekable stream is consumed. Set <see cref="CsvInferenceOptions.RestoreStreamPosition"/>
    /// to false to leave a seekable stream at the parser's final buffered position.
    /// </summary>
    public static CsvInferenceResult Infer(Stream stream, CsvInferenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        return InferCore(stream, options ?? new CsvInferenceOptions());
    }

    private static CsvInferenceResult InferCore(Stream stream, CsvInferenceOptions options)
    {
        Validate(options);
        var diagnostics = new List<InferenceDiagnostic>();
        var start = stream.CanSeek ? stream.Position : (long?)null;
        Stream sampleStream = stream;
        MemoryStream? snapshot = null;

        try
        {
            if (options.InferDialect || options.MaximumBytes.HasValue)
            {
                snapshot = Capture(stream, options.MaximumBytes, out var truncated);
                sampleStream = snapshot;
                if (truncated)
                    diagnostics.Add(new("sample.byte-limit", $"Sampling stopped at the configured {options.MaximumBytes} byte limit."));
            }

            var dialect = options.Dialect with { Header = false, HeaderRows = [] };
            if (options.InferDialect)
            {
                dialect = InferDialect(snapshot!, options, diagnostics);
                snapshot!.Position = 0;
            }

            var records = ReadRecords(sampleStream, dialect, options.Resource, options.MaximumRecords);
            if (records.Count == 0)
                throw new InvalidDataException("No CSV records were available for inference.");

            var hasHeader = InferHeader(records, options, diagnostics);
            var finalDialect = dialect with { Header = hasHeader, HeaderRows = hasHeader ? [1] : [] };
            var data = hasHeader ? records.Skip(1).ToList() : records;
            var width = records.Max(record => record.Count);
            var names = CreateNames(hasHeader ? records[0] : null, width, diagnostics);
            var columns = InferColumns(data, width, names, options.Culture, diagnostics);
            var schema = CreateSchema(columns, options.Culture);

            if (records.Select(record => record.Count).Distinct().Skip(1).Any())
                diagnostics.Add(new("records.inconsistent-width", "Sampled records do not all contain the same number of fields.", InferenceDiagnosticSeverity.Warning));

            return new(finalDialect, schema, records.Count, columns, diagnostics);
        }
        finally
        {
            snapshot?.Dispose();
            if (start.HasValue && options.RestoreStreamPosition)
                stream.Position = start.Value;
        }
    }

    private static void Validate(CsvInferenceOptions options)
    {
        if (options.MaximumRecords <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumRecords), "MaximumRecords must be positive.");
        if (options.MaximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumBytes), "MaximumBytes must be positive when specified.");
        ArgumentNullException.ThrowIfNull(options.Culture);
        ArgumentNullException.ThrowIfNull(options.Dialect);
    }

    private static ResourceDescriptor? InferResource(string filename)
    {
        var extension = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
        return extension is "gz" or "gzip" or "zip" or "deflate" or "def" or "zz"
            ? new ResourceDescriptor(Compression: extension)
            : null;
    }

    private static MemoryStream Capture(Stream source, long? maximumBytes, out bool truncated)
    {
        var result = new MemoryStream();
        var buffer = new byte[81920];
        long remaining = maximumBytes ?? long.MaxValue;
        while (remaining > 0)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = source.Read(buffer, 0, requested);
            if (read == 0)
                break;
            result.Write(buffer, 0, read);
            remaining -= read;
        }
        truncated = maximumBytes.HasValue && remaining == 0
            && (!source.CanSeek || source.Position < source.Length);
        result.Position = 0;
        return result;
    }

    private static DialectDescriptor InferDialect(
        MemoryStream snapshot,
        CsvInferenceOptions options,
        List<InferenceDiagnostic> diagnostics)
    {
        var text = ReadText(snapshot, options.Resource);
        var lineTerminator = DetectLineTerminator(text, options.Dialect.LineTerminator);
        var delimiters = new[] { options.Dialect.Delimiter, ',', '\t', ';', '|' }.Distinct();
        var quotes = new char?[] { options.Dialect.QuoteChar, '"', '\'', null }.Distinct();
        var escape = DetectEscape(text, options.Dialect.EscapeChar);
        var candidates = new List<(DialectDescriptor Dialect, double Score)>();

        foreach (var delimiter in delimiters)
        foreach (var quote in quotes)
        {
            var candidate = options.Dialect with
            {
                Header = false,
                HeaderRows = [],
                Delimiter = delimiter,
                LineTerminator = lineTerminator,
                QuoteChar = quote,
                EscapeChar = escape
            };
            try
            {
                snapshot.Position = 0;
                var records = ReadRecords(snapshot, candidate, options.Resource, Math.Min(options.MaximumRecords, 100));
                if (records.Count == 0)
                    continue;
                var groups = records.GroupBy(record => record.Count).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key).ToArray();
                var modalWidth = groups[0].Key;
                var consistency = (double)groups[0].Count() / records.Count;
                var widthScore = modalWidth > 1 ? Math.Min(modalWidth, 6) / 6d : 0;
                var quoteEvidence = quote.HasValue && text.Contains(quote.Value) ? 0.05 : 0;
                var score = consistency * 0.65 + widthScore * 0.25 + Math.Min(records.Count, 10) / 100d + quoteEvidence;
                candidates.Add((candidate, score));
            }
            catch (InvalidDataException)
            {
                // Invalid candidates are expected while evaluating alternative dialects.
            }
        }

        if (candidates.Count == 0)
        {
            diagnostics.Add(new("dialect.unresolved", "No candidate dialect parsed the sample; the configured dialect was retained.", InferenceDiagnosticSeverity.Warning, 0));
            return options.Dialect with { Header = false, HeaderRows = [], LineTerminator = lineTerminator };
        }

        var ranked = candidates.OrderByDescending(candidate => candidate.Score).ToArray();
        var best = ranked[0];
        var confidence = ranked.Length == 1 ? 1 : Math.Clamp(best.Score - ranked[1].Score + 0.5, 0, 1);
        diagnostics.Add(new(
            confidence < 0.6 ? "dialect.ambiguous" : "dialect.inferred",
            $"Selected delimiter {Display(best.Dialect.Delimiter)}, quote {Display(best.Dialect.QuoteChar)}, and line terminator {Display(best.Dialect.LineTerminator)}.",
            confidence < 0.6 ? InferenceDiagnosticSeverity.Warning : InferenceDiagnosticSeverity.Information,
            confidence));
        return best.Dialect;
    }

    private static string ReadText(MemoryStream snapshot, ResourceDescriptor? resource)
    {
        snapshot.Position = 0;
        var processed = OpenProcessedStream(snapshot, resource, out var disposeProcessed);
        try
        {
            using var reader = CreateStreamReader(processed, resource, leaveOpen: true);
            return reader.ReadToEnd();
        }
        finally
        {
            if (disposeProcessed)
                processed.Dispose();
            snapshot.Position = 0;
        }
    }

    private static string DetectLineTerminator(string text, string fallback)
    {
        var crlf = Count(text, "\r\n");
        var lf = Count(text, "\n") - crlf;
        var cr = Count(text, "\r") - crlf;
        if (crlf == 0 && lf == 0 && cr == 0)
            return fallback;
        return crlf >= lf && crlf >= cr ? "\r\n" : lf >= cr ? "\n" : "\r";
    }

    private static int Count(string value, string token)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0; index += token.Length)
            count++;
        return count;
    }

    private static char? DetectEscape(string text, char? fallback)
        => text.Contains("\\\"") || text.Contains("\\'") ? '\\' : fallback;

    private static string Display(char? value) => value.HasValue ? $"'{(value == '\t' ? "\\t" : value)}'" : "none";
    private static string Display(string value) => value.Replace("\r", "\\r").Replace("\n", "\\n");

    private static List<List<SampleCell>> ReadRecords(Stream source, DialectDescriptor dialect, ResourceDescriptor? resource, int maximumRecords)
    {
        var processed = OpenProcessedStream(source, resource, out var disposeProcessed);
        try
        {
            using var reader = CreateStreamReader(processed, resource, leaveOpen: true);
            var profile = new CsvProfile(dialect with { Header = false, HeaderRows = [] }, resource: resource)
            {
                ParserOptimizations = new ParserOptimizationOptions(BufferSize: 4096)
            };
            using var parser = new RecordParser(reader, profile);
            var sanitizer = new SanitizerFactory(profile).Create(profile.Resource?.Sequences, new FieldEscaper(profile));
            var records = new List<List<SampleCell>>();
            var physicalRow = 0;

            while (records.Count < maximumRecords)
            {
                var eof = parser.IsEndOfFile(out var record, out var state);
                if (state == RecordState.Eof || (eof && record.FieldSpans.Length == 0))
                    break;
                physicalRow++;
                if (state == RecordState.Comment || (dialect.CommentRows?.Contains(physicalRow) ?? false))
                {
                    if (eof) break;
                    continue;
                }

                var values = new List<SampleCell>(record.FieldSpans.Length);
                for (var index = 0; index < record.FieldSpans.Length; index++)
                {
                    var field = record.FieldSpans[index].Value;
                    var raw = record.Slice(index);
                    var value = sanitizer.Sanitize(raw, field.IsEscaped, field.WasQuoted);
                    values.Add(new(value.HasValue ? value.Value.ToString() : null, raw.Length == 0));
                }
                records.Add(values);
                if (eof) break;
            }
            return records;
        }
        finally
        {
            if (disposeProcessed)
                processed.Dispose();
        }
    }

    private static Stream OpenProcessedStream(Stream source, ResourceDescriptor? resource, out bool disposeProcessed)
    {
        if (string.IsNullOrWhiteSpace(resource?.Compression))
        {
            disposeProcessed = false;
            return source;
        }
        var factory = source.CanSeek ? DecompressorFactory.Buffered() : DecompressorFactory.Streaming();
        disposeProcessed = true;
        return factory.GetDecompressor(resource.Compression).Decompress(source);
    }

    private static StreamReader CreateStreamReader(Stream stream, ResourceDescriptor? resource, bool leaveOpen)
    {
        if (!stream.CanSeek)
            return new StreamReader(stream, GetEncoding(resource?.Encoding), detectEncodingFromByteOrderMarks: true, 1024, leaveOpen);

        var start = stream.Position;
        var encoding = new EncodingDetector().GetStreamEncoding(stream, resource?.Encoding);
        stream.Position = start + encoding.BomBytesCount;
        return new StreamReader(stream, encoding.Encoding, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen);
    }

    private static Encoding GetEncoding(string? name)
    {
        if (name is null)
            return Encoding.UTF8;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(name);
    }

    private static bool InferHeader(List<List<SampleCell>> records, CsvInferenceOptions options, List<InferenceDiagnostic> diagnostics)
    {
        if (options.Header != CsvHeaderMode.Auto)
        {
            var present = options.Header == CsvHeaderMode.Present;
            diagnostics.Add(new("header.explicit", $"Header presence was explicitly set to {present.ToString().ToLowerInvariant()}.", Confidence: 1));
            return present;
        }

        if (records.Count < 2)
        {
            diagnostics.Add(new("header.ambiguous", "A single sampled record is insufficient for reliable header detection; it was treated as data.", InferenceDiagnosticSeverity.Warning, 0));
            return false;
        }

        var first = records[0];
        var widthStable = records.Skip(1).All(record => record.Count == first.Count);
        var names = first.Select(cell => cell.Value?.Trim()).ToArray();
        var uniqueNames = names.All(name => !string.IsNullOrEmpty(name)) && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length;
        var nameLike = uniqueNames && names.All(name => name!.Any(char.IsLetter));
        var compared = 0;
        var different = 0;
        var matchingTyped = 0;
        for (var column = 0; column < first.Count; column++)
        {
            var later = records.Skip(1).Where(record => column < record.Count && record[column].Value is not null).Select(record => record[column].Value!).ToArray();
            if (later.Length == 0 || first[column].Value is null)
                continue;
            compared++;
            var firstType = InferRuntimeType([first[column].Value!], options.Culture);
            var laterType = InferRuntimeType(later, options.Culture);
            if (firstType == typeof(string) && laterType != typeof(string))
                different++;
            else if (firstType != typeof(string) && firstType == laterType)
                matchingTyped++;
        }

        var typeDifference = compared == 0 ? 0 : (double)different / compared;
        var typedSimilarity = compared == 0 ? 0 : (double)matchingTyped / compared;
        var confidence = Math.Clamp((nameLike ? 0.25 : 0) + (widthStable ? 0.1 : 0) + typeDifference * 0.65 - typedSimilarity * 0.25, 0, 1);
        var hasHeader = confidence >= 0.55;
        diagnostics.Add(new(
            confidence is >= 0.25 and < 0.75 ? "header.ambiguous" : "header.inferred",
            hasHeader ? "The first sampled record was inferred to be a header." : "The first sampled record was inferred to contain data.",
            confidence is >= 0.25 and < 0.75 ? InferenceDiagnosticSeverity.Warning : InferenceDiagnosticSeverity.Information,
            confidence));
        return hasHeader;
    }

    private static string[] CreateNames(List<SampleCell>? header, int width, List<InferenceDiagnostic> diagnostics)
    {
        var names = new string[width];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < width; index++)
        {
            var basis = header is not null && index < header.Count ? header[index].Value?.Trim() : null;
            basis = string.IsNullOrWhiteSpace(basis) ? $"Column{index + 1}" : basis;
            var name = basis;
            for (var suffix = 2; !used.Add(name); suffix++)
                name = $"{basis}_{suffix}";
            names[index] = name;
            if (!name.Equals(basis, StringComparison.Ordinal))
                diagnostics.Add(new("header.duplicate-name", $"Duplicate column name '{basis}' was renamed to '{name}'.", InferenceDiagnosticSeverity.Warning));
        }
        return names;
    }

    private static List<CsvInferredColumn> InferColumns(
        List<List<SampleCell>> records,
        int width,
        string[] names,
        IFormatProvider culture,
        List<InferenceDiagnostic> diagnostics)
    {
        var result = new List<CsvInferredColumn>(width);
        for (var column = 0; column < width; column++)
        {
            var values = new List<string>();
            var nulls = 0;
            var empty = 0;
            var missing = 0;
            foreach (var record in records)
            {
                if (column >= record.Count)
                {
                    missing++;
                    continue;
                }
                var cell = record[column];
                if (cell.WasEmpty) empty++;
                if (cell.Value is null)
                    nulls++;
                else if (cell.Value.Length > 0)
                    values.Add(cell.Value);
            }

            var runtimeType = InferRuntimeType(values, culture);
            if (values.Count == 0)
                diagnostics.Add(new("column.all-null", $"Column '{names[column]}' contained only null, empty, or missing values and fell back to String.", InferenceDiagnosticSeverity.Warning));
            if (runtimeType == typeof(string) && values.Count > 1 && values.Any(HasSignificantLeadingZero))
                diagnostics.Add(new("column.leading-zero", $"Column '{names[column]}' was kept as String because sampled values contain significant leading zeroes."));
            result.Add(new(names[column], runtimeType, nulls > 0 || empty > 0 || missing > 0 || values.Count == 0, values.Count, nulls, empty, missing));
        }
        return result;
    }

    private static Type InferRuntimeType(IEnumerable<string> source, IFormatProvider culture)
    {
        var values = source.ToArray();
        if (values.Length == 0 || values.Any(HasSignificantLeadingZero)) return typeof(string);
        if (values.All(value => bool.TryParse(value, out _))) return typeof(bool);
        if (values.All(value => short.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, culture, out _))) return typeof(short);
        if (values.All(value => int.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, culture, out _))) return typeof(int);
        if (values.All(value => long.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, culture, out _))) return typeof(long);
        if (values.All(value => decimal.TryParse(value, NumberStyles.Number, culture, out _))) return typeof(decimal);
        if (values.All(value => double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, culture, out _))) return typeof(double);
        if (values.All(value => DateOnly.TryParseExact(value, DatePatterns, culture, DateTimeStyles.None, out _))) return typeof(DateOnly);
        if (values.All(value => TimeOnly.TryParseExact(value, TimePatterns, culture, DateTimeStyles.None, out _))) return typeof(TimeOnly);
        if (values.All(value => DateTimeOffset.TryParseExact(value, DateTimeOffsetPatterns, culture, DateTimeStyles.None, out _))) return typeof(DateTimeOffset);
        if (values.All(value => DateTime.TryParseExact(value, DateTimePatterns, culture, DateTimeStyles.None, out _))) return typeof(DateTime);
        if (values.All(value => Guid.TryParse(value, out _))) return typeof(Guid);
        return typeof(string);
    }

    private static bool HasSignificantLeadingZero(string value)
    {
        var span = value.AsSpan();
        if (span.Length > 0 && (span[0] == '+' || span[0] == '-')) span = span[1..];
        return span.Length > 1 && span[0] == '0' && span[1..].IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static SchemaDescriptor CreateSchema(IReadOnlyList<CsvInferredColumn> columns, IFormatProvider culture)
    {
        var schema = new SchemaDescriptor.IndexedSchemaDescriptor();
        foreach (var column in columns)
        {
            IFormatDescriptor? format = column.RuntimeType switch
            {
                var type when type == typeof(short) || type == typeof(int) || type == typeof(long)
                    => new NumericFormatDescriptor(NumberStyles.Integer | NumberStyles.AllowThousands, culture),
                var type when type == typeof(decimal)
                    => new NumericFormatDescriptor(NumberStyles.Number, culture),
                var type when type == typeof(double)
                    => new NumericFormatDescriptor(NumberStyles.Float | NumberStyles.AllowThousands, culture),
                _ => null
            };
            ParseFunction? parse = column.RuntimeType switch
            {
                var type when type == typeof(DateOnly) => value => DateOnly.ParseExact(value, DatePatterns, culture),
                var type when type == typeof(TimeOnly) => value => TimeOnly.ParseExact(value, TimePatterns, culture),
                var type when type == typeof(DateTime) => value => DateTime.ParseExact(value, DateTimePatterns, culture),
                var type when type == typeof(DateTimeOffset) => value => DateTimeOffset.ParseExact(value, DateTimeOffsetPatterns, culture),
                _ => null
            };
            schema.Fields.Add(new FieldDescriptor(column.RuntimeType, column.Name, format, parse, IsNullable: column.IsNullable));
        }
        return schema;
    }

    private readonly record struct SampleCell(string? Value, bool WasEmpty);
}
