using System;

namespace PocketCsvReader;

public class CsvProfile
{
    public CsvDialectDescriptor Descriptor { get; private set; }
    public OptimizationOptions ParserOptimizations { get; set; }
    public virtual int BufferSize { get; private set; }
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

        ParserOptimizations = new OptimizationOptions() { RowCountAtStart = rowCountAtStart };

        EmptyCell = emptyCell;
        MissingCell = missingCell;
        BufferSize = bufferSize;
    }

    public CsvProfile(CsvDialectDescriptor descriptor)
    {
        if (descriptor.DoubleQuote)
            throw new ArgumentException("PocketCsvReader doesn't support doubleQuote set to true in the CSV dialect descriptor.");
        if (descriptor.NullSequence?.Length > 0)
            throw new ArgumentException("PocketCsvReader doesn't support nullSequence set to any value in the CSV dialect descriptor.");
        if (descriptor.SkipInitialSpace)
            throw new ArgumentException("PocketCsvReader doesn't support skipInitialSpace set to true in the CSV dialect descriptor.");
        if (descriptor.CaseSensitiveHeader)
            throw new ArgumentException("PocketCsvReader doesn't support caseSensitiveHeader set to true in the CSV dialect descriptor.");

        Descriptor = descriptor;
        ParserOptimizations = new OptimizationOptions();
        EmptyCell = string.Empty;
        MissingCell = string.Empty;
        BufferSize = 4096;
    }

    public static CsvProfile CommaDoubleQuote { get; } = new CsvProfile(',', '\"');
    public static CsvProfile SemiColumnDoubleQuote { get; } = new CsvProfile(';', '\"');
    public static CsvProfile TabDoubleQuote { get; } = new CsvProfile('\t', '\"');
}
