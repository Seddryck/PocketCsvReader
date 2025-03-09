using System;
using System.Collections.Immutable;
using System.Reflection;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;

public class CsvProfile
{
    public DialectDescriptor Dialect { get; private set; }
    public SchemaDescriptor? Schema { get; private set; }
    public ResourceDescriptor? Resource { get; private set; }
    public RuntimeParsersDescriptor? Parsers { get; private set; }

    public ParserOptimizationOptions ParserOptimizations { get; set; }

    public virtual string EmptyCell { get; private set; }
    public virtual string MissingCell { get; private set; }

    public CsvProfile(char fieldSeparator, string recordSeparator)
        : this(fieldSeparator, '\"', recordSeparator, false)
    { }

    public CsvProfile(char fieldSeparator, char textQualifier, string recordSeparator, bool firstRowHeader)
        : this(fieldSeparator, textQualifier, textQualifier, recordSeparator, firstRowHeader, false, 4096, string.Empty, string.Empty)
    { }

    public CsvProfile(char fieldSeparator, char textQualifier, char escapeTextQualifier, string recordSeparator, bool firstRowHeader, bool rowCountAtStart, int bufferSize, string emptyCell, string missingCell)
    {
        Dialect = new DialectDescriptorBuilder()
                        .WithDelimiter(fieldSeparator)
                        .WithLineTerminator(recordSeparator)
                        .WithQuoteChar(textQualifier)
                        .WithEscapeChar(escapeTextQualifier)
                        .WithHeader(firstRowHeader)
                        .Build();

        ParserOptimizations = new ParserOptimizationOptions() { RowCountAtStart = rowCountAtStart, BufferSize = bufferSize };

        EmptyCell = emptyCell;
        Resource = new ResourceDescriptorBuilder()
            .WithSequence(string.Empty, emptyCell)
            .Also(r => { if (Dialect.NullSequence is not null) r.WithSequence(missingCell, null); })
            .Build();
        MissingCell = missingCell;
    }

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
        MissingCell = string.Empty;

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
