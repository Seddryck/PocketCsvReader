using System;

namespace PocketCsvReader;

public class CsvProfile
{
    public CsvDialectDescriptor Descriptor { get; private set; }
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
        Descriptor = new CsvDialectDescriptor
        {
            Delimiter = fieldSeparator,
            QuoteChar = textQualifier,
            EscapeChar = escapeTextQualifier,
            LineTerminator = recordSeparator,
            Header = firstRowHeader
        };

        ParserOptimizations = new ParserOptimizationOptions() { RowCountAtStart = rowCountAtStart, BufferSize = bufferSize };

        EmptyCell = emptyCell;
        MissingCell = missingCell;
    }

    public CsvProfile(CsvDialectDescriptor descriptor)
    {
        if (descriptor.DoubleQuote)
            throw new ArgumentException("PocketCsvReader doesn't support doubleQuote set to true in the CSV dialect descriptor.");
        if (descriptor.CaseSensitiveHeader)
            throw new ArgumentException("PocketCsvReader doesn't support caseSensitiveHeader set to true in the CSV dialect descriptor.");

        if (descriptor.NullSequence is not null)
            Sequences.Add(descriptor.NullSequence, null);

        Descriptor = descriptor;
        ParserOptimizations = new ParserOptimizationOptions();
        EmptyCell = string.Empty;
        MissingCell = string.Empty;
    }

    public static CsvProfile CommaDoubleQuote { get; } = new CsvProfile(',', '\"');
    public static CsvProfile SemiColumnDoubleQuote { get; } = new CsvProfile(';', '\"');
    public static CsvProfile TabDoubleQuote { get; } = new CsvProfile('\t', '\"');
}
