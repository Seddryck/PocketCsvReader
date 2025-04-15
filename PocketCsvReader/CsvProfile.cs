using System;
using System.Collections.Immutable;
using System.Reflection;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;

public class CsvProfile : IProfile
{
    public DialectDescriptor Dialect { get; private set; }
    public SchemaDescriptor? Schema { get; private set; }
    public ResourceDescriptor? Resource { get; private set; }
    public RuntimeParsersDescriptor? Parsers { get; private set; }

    public ParserOptimizationOptions ParserOptimizations { get; set; }

    public virtual string EmptyCell { get; private set; }

    /// <summary>
    /// Initializes a new CsvProfile with the specified field separator and record separator, using double quotes as the text qualifier and no header row.
    /// </summary>
    /// <param name="fieldSeparator">The character used to separate fields.</param>
    /// <param name="recordSeparator">The string used to separate records (rows).</param>
    public CsvProfile(char fieldSeparator, string recordSeparator)
        : this(fieldSeparator, '\"', recordSeparator, false)
    { }

    public CsvProfile(char fieldSeparator, char textQualifier, string recordSeparator, bool firstRowHeader)
        : this(fieldSeparator, textQualifier, textQualifier, recordSeparator, firstRowHeader, false, 4096, string.Empty, string.Empty)
    { }

    /// <summary>
    /// Initializes a new <see cref="CsvProfile"/> with custom CSV dialect, parser optimization options, and resource sequences for empty and missing cells.
    /// </summary>
    /// <param name="fieldSeparator">The character used to separate fields in the CSV.</param>
    /// <param name="textQualifier">The character used to quote fields.</param>
    /// <param name="escapeTextQualifier">The character used to escape the text qualifier within quoted fields.</param>
    /// <param name="recordSeparator">The string used to separate records (rows).</param>
    /// <param name="firstRowHeader">Indicates whether the first row contains column headers.</param>
    /// <param name="rowCountAtStart">If true, expects the row count at the start of the CSV.</param>
    /// <param name="bufferSize">The buffer size for parsing operations.</param>
    /// <param name="emptyCell">The string representing an empty cell in the CSV.</param>
    /// <param name="missingCell">The string representing a missing cell in the CSV.</param>
    public CsvProfile(char fieldSeparator, char textQualifier, char escapeTextQualifier, string recordSeparator, bool firstRowHeader, bool rowCountAtStart, int bufferSize, string emptyCell, string missingCell)
    {
        Dialect = new DialectDescriptorBuilder()
                        .WithDelimiter(fieldSeparator)
                        .WithLineTerminator(recordSeparator)
                        .WithQuoteChar(textQualifier)
                        .WithEscapeChar(escapeTextQualifier)
                        .WithHeader(firstRowHeader)
                        .WithMissingCell(missingCell)
                        .Build();

        ParserOptimizations = new ParserOptimizationOptions() { RowCountAtStart = rowCountAtStart, BufferSize = bufferSize };

        EmptyCell = emptyCell;
        Resource = new ResourceDescriptorBuilder()
            .WithSequence(string.Empty, emptyCell)
            .Also(r => { if (Dialect.NullSequence is not null) r.WithSequence(missingCell, null); })
            .Build();
    }

    /// <summary>
    /// Initializes a new CsvProfile using the specified dialect, schema, resource, and parsers.
    /// </summary>
    /// <param name="dialect">The CSV dialect descriptor to use for parsing configuration.</param>
    /// <param name="schema">Optional schema descriptor for column definitions.</param>
    /// <param name="resource">Optional resource descriptor for cell value sequences; if the dialect defines a null sequence, it is added to the resource.</param>
    /// <param name="parsers">Optional runtime parsers descriptor.</param>
    public CsvProfile(DialectDescriptor dialect, SchemaDescriptor? schema = null, ResourceDescriptor? resource = null, RuntimeParsersDescriptor? parsers = null)
    {
        if (dialect.NullSequence is not null)
            resource = (resource ??= new ResourceDescriptor()) with
            {
                Sequences = SequenceCollection
                    .Concat(resource.Sequences, ImmutableSequenceCollection.Empty)
                    .Also(seq => seq.Add(dialect.NullSequence, null))
                    .ToImmutable()
            };

        Dialect = dialect;
        ParserOptimizations = new ParserOptimizationOptions();
        EmptyCell = string.Empty;

        Schema = schema;
        Resource = resource;
        Parsers = parsers;
    }

    private static CsvProfile? _commaDoubleQuote;
    public static CsvProfile CommaDoubleQuote
    {
        get => _commaDoubleQuote ??= new CsvProfile(new DialectDescriptorBuilder()
                                        .WithDelimiter(Delimiter.Comma)
                                        .WithLineTerminator(Environment.NewLine)
                                        .WithQuoteChar(QuoteChar.SingleQuote)
                                        .WithEscapeChar(EscapeChar.BackSlash)
                                        .WithDoubleQuote(true)
                                        .WithoutHeader()
                                        .Build());
    }

    private static CsvProfile? _semiColumnDoubleQuote;
    public static CsvProfile SemiColumnDoubleQuote
    {
        get => _semiColumnDoubleQuote ??= new CsvProfile(new DialectDescriptorBuilder()
                                        .WithDelimiter(Delimiter.Semicolon)
                                        .WithLineTerminator(Environment.NewLine)
                                        .WithQuoteChar(QuoteChar.SingleQuote)
                                        .WithEscapeChar(EscapeChar.BackSlash)
                                        .WithDoubleQuote(true)
                                        .WithoutHeader()
                                        .Build());
    }

    private static CsvProfile? _tabDoubleQuote;
    public static CsvProfile TabDoubleQuote
    {
        get => _tabDoubleQuote ??= new CsvProfile(new DialectDescriptorBuilder()
                                        .WithDelimiter(Delimiter.Tab)
                                        .WithLineTerminator(Environment.NewLine)
                                        .WithQuoteChar(QuoteChar.SingleQuote)
                                        .WithEscapeChar(EscapeChar.BackSlash)
                                        .WithDoubleQuote(true)
                                        .WithoutHeader()
                                        .Build());
    }

    private static CsvProfile? _pipeSingleQuote;
    public static CsvProfile PipeSingleQuote
    {
        get => _pipeSingleQuote ??= new CsvProfile(new DialectDescriptorBuilder()
                                        .WithDelimiter(Delimiter.Pipe)
                                        .WithLineTerminator(Environment.NewLine)
                                        .WithQuoteChar(QuoteChar.SingleQuote)
                                        .WithEscapeChar(EscapeChar.BackSlash)
                                        .WithDoubleQuote(true)
                                        .WithoutHeader()
                                        .Build());
    }
}
