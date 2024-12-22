using System;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;

public class CsvProfile
{
    public DialectDescriptor Descriptor { get; private set; }
    public ParserOptimizationOptions ParserOptimizations { get; set; }
    public Dictionary<string, string?> Sequences { get; } = new();

    public virtual string EmptyCell { get; private set; }
    public virtual string MissingCell { get; private set; }

    protected CsvProfile()
        : this(',', '\"')
    { }

    public CsvProfile(bool firstRowHeader)
        : this(',', '\"', "\r\n", firstRowHeader)
    { }

    public CsvProfile(char fieldSeparator, char textQualifier)
        : this(fieldSeparator, textQualifier, "\r\n")
    { }

    public CsvProfile(char fieldSeparator, string recordSeparator)
        : this(fieldSeparator, '\"', recordSeparator)
    { }

    public CsvProfile(char fieldSeparator, char textQualifier, string recordSeparator)
        : this(fieldSeparator, textQualifier, recordSeparator, false)
    { }

    public CsvProfile(char fieldSeparator, char textQualifier, string recordSeparator, bool firstRowHeader)
        : this(fieldSeparator, textQualifier, textQualifier, recordSeparator, firstRowHeader, false, 4096, string.Empty, string.Empty)
    { }

    public CsvProfile(char fieldSeparator, char textQualifier, string recordSeparator, bool firstRowHeader, bool performanceOptimized, int bufferSize)
        : this(fieldSeparator, textQualifier, textQualifier, recordSeparator, firstRowHeader, performanceOptimized, bufferSize, string.Empty, string.Empty)
    { }

    public CsvProfile(char fieldSeparator, char textQualifier, string recordSeparator, bool firstRowHeader, bool performanceOptimized, int bufferSize, string emptyCell, string missingCell)
        : this(fieldSeparator, textQualifier, textQualifier, recordSeparator, firstRowHeader, performanceOptimized, bufferSize, emptyCell, missingCell)
    { }

    public CsvProfile(char fieldSeparator, char textQualifier, char escapeTextQualifier, string recordSeparator, bool firstRowHeader, bool rowCountAtStart, int bufferSize, string emptyCell, string missingCell)
    {
        Descriptor = new DialectDescriptorBuilder()
                        .WithDelimiter(fieldSeparator)
                        .WithLineTerminator(recordSeparator)
                        .WithQuoteChar(textQualifier)
                        .WithEscapeChar(escapeTextQualifier)
                        .WithHeader(firstRowHeader)
                        .Build();

        ParserOptimizations = new ParserOptimizationOptions() { RowCountAtStart = rowCountAtStart, BufferSize = bufferSize };

        EmptyCell = emptyCell;
        MissingCell = missingCell;
    }

    public CsvProfile(DialectDescriptor descriptor)
    {
        if (descriptor.NullSequence is not null)
            Sequences.Add(descriptor.NullSequence, null);

        Descriptor = descriptor;
        ParserOptimizations = new ParserOptimizationOptions();
        EmptyCell = string.Empty;
        MissingCell = string.Empty;
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
