using System;

namespace PocketCsvReader
{
    public class CsvProfile
    {
        public virtual char FieldSeparator { get; private set; }
        public virtual char TextQualifier { get; private set; }
        public virtual char EscapeTextQualifier { get; private set; }
        public virtual string RecordSeparator { get; private set; }
        public virtual bool FirstRowHeader { get; private set; }
        public virtual bool PerformanceOptmized { get; private set; }
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
            : this(fieldSeparator, textQualifier, recordSeparator, true)
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

        public CsvProfile(char fieldSeparator, char textQualifier, char escapeTextQualifier, string recordSeparator, bool firstRowHeader, bool performanceOptimized, int bufferSize, string emptyCell, string missingCell)
        {
            FieldSeparator = fieldSeparator;
            TextQualifier = textQualifier;
            EscapeTextQualifier = escapeTextQualifier;
            RecordSeparator = recordSeparator;
            FirstRowHeader = firstRowHeader;
            PerformanceOptmized = performanceOptimized;
            EmptyCell = emptyCell;
            MissingCell = missingCell;
            BufferSize = bufferSize;
        }

        public static CsvProfile CommaDoubleQuote { get; } = new CsvProfile(',', '\"');
        public static CsvProfile SemiColumnDoubleQuote { get; } = new CsvProfile(';', '\"');
        public static CsvProfile TabDoubleQuote { get; } = new CsvProfile('\t', '\"');
    }
}
