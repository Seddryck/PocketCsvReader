using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class DialectDescriptorBuilder
{
    private DialectDescriptor Descriptor { get; set; } = new();
    private ArrayDialectDescriptorBuilder? ArrayBuilder { get; set; }

    public DialectDescriptorBuilder WithDelimiter(char delimiter)
        => (Descriptor = Descriptor with { Delimiter = delimiter }, Builder: this).Builder;
    public DialectDescriptorBuilder WithDelimiter(Delimiter delimiter)
        => WithDelimiter((char)delimiter);
    public DialectDescriptorBuilder WithLineTerminator(string lineTerminator)
        => (Descriptor = Descriptor with { LineTerminator = lineTerminator }, Builder: this).Builder;
    public DialectDescriptorBuilder WithLineTerminator(LineTerminator lineTerminator)
    {
        var terminator = lineTerminator switch
        {
            LineTerminator.CarriageReturnLineFeed => "\r\n",
            LineTerminator.LineFeed => "\n",
            LineTerminator.CarriageReturn => "\r",
            _ => throw new ArgumentOutOfRangeException(nameof(lineTerminator), lineTerminator, null)
        };
        return WithLineTerminator(terminator);
    }
    public DialectDescriptorBuilder WithQuoteChar(char? quoteChar)
        => (Descriptor = Descriptor with { QuoteChar = quoteChar }, Builder: this).Builder;
    public DialectDescriptorBuilder WithQuoteChar(QuoteChar quoteChar)
        => WithQuoteChar((char)quoteChar);
    public DialectDescriptorBuilder WithoutQuoteChar()
        => (Descriptor = Descriptor with { QuoteChar = null }, Builder: this).Builder;
    public DialectDescriptorBuilder WithDoubleQuote(bool doubleQuote = true)
        => (Descriptor = Descriptor with { DoubleQuote = doubleQuote }, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutDoubleQuote()
        => WithDoubleQuote(false);
    public DialectDescriptorBuilder WithEscapeChar(char? escapeChar)
        => (Descriptor = Descriptor with { EscapeChar = escapeChar}, Builder: this).Builder;
    public DialectDescriptorBuilder WithEscapeChar(EscapeChar escapeChar)
        => WithEscapeChar((char)escapeChar);
    public DialectDescriptorBuilder WithoutEscapeChar()
        => (Descriptor = Descriptor with { EscapeChar = null }, Builder: this).Builder;
    /// <summary>
        /// Sets the string used to represent null values in the CSV dialect.
        /// </summary>
        /// <param name="nullSequence">The string to use for null values, or null to clear the setting.</param>
        /// <returns>The current builder instance for chaining.</returns>
        public DialectDescriptorBuilder WithNullSequence(string? nullSequence)
        => (Descriptor = Descriptor with { NullSequence = nullSequence}, Builder: this).Builder;
    /// <summary>
        /// Removes the null sequence setting from the dialect configuration.
        /// </summary>
        /// <returns>The current builder instance for chaining.</returns>
        public DialectDescriptorBuilder WithoutNullSequence()
        => WithNullSequence(null);
    /// <summary>
        /// Sets the string used to represent missing cells in the CSV dialect configuration.
        /// </summary>
        /// <param name="value">The string to use for missing cells.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        public DialectDescriptorBuilder WithMissingCell(string value)
        => (Descriptor = Descriptor with { MissingCell = value }, Builder: this).Builder;
    /// <summary>
        /// Sets whether to skip initial whitespace after delimiters when parsing fields.
        /// </summary>
        /// <param name="skipInitialSpace">If true, leading spaces after delimiters are ignored; otherwise, they are preserved.</param>
        /// <returns>The current builder instance for chaining.</returns>
        public DialectDescriptorBuilder WithSkipInitialSpace(bool skipInitialSpace = true)
        => (Descriptor = Descriptor with { SkipInitialSpace = skipInitialSpace}, Builder: this).Builder;
    /// <summary>
        /// Disables skipping of initial spaces in fields during CSV parsing.
        /// </summary>
        /// <returns>The current builder instance for chaining.</returns>
        public DialectDescriptorBuilder WithoutSkipInitialSpace()
        => WithSkipInitialSpace(false);
    public DialectDescriptorBuilder WithHeader(bool header = true)
    {
        if (header != Descriptor.Header)
        {
            Descriptor = Descriptor with
            {
                Header = header,
                HeaderRows = header ? [1] : []
            };
        }
        return this;
    }
    public DialectDescriptorBuilder WithoutHeader()
        => WithHeader(false);
    public DialectDescriptorBuilder WithHeaderJoin(string join)
        => (Descriptor = Descriptor with { HeaderJoin = join }, Builder: this).Builder;
    public DialectDescriptorBuilder WithHeaderRows(int[] headerRows)
    {
        if (headerRows is null || headerRows.Length == 0)
            return WithoutHeader();
        return (Descriptor = Descriptor with { HeaderRows = headerRows }, Builder: this).Builder;
    }
    public DialectDescriptorBuilder WithHeaderRepeat(bool value)
        => (Descriptor = Descriptor with { HeaderRepeat = value }, Builder: this).Builder;
    public DialectDescriptorBuilder WithHeaderRepeat()
        => WithHeaderRepeat(true);
    public DialectDescriptorBuilder WithoutHeaderRepeat()
        => WithHeaderRepeat(false);
    public DialectDescriptorBuilder WithoutHeaderRows()
        => WithHeaderRows([]);
    public DialectDescriptorBuilder WithCommentChar(char? commentChar)
        => (Descriptor = Descriptor with { CommentChar = commentChar}, Builder: this).Builder;
    public DialectDescriptorBuilder WithCommentChar(CommentChar commentChar)
       => WithCommentChar((char)commentChar);
    public DialectDescriptorBuilder WithCommentRows(int[] commentRows)
        => (Descriptor = Descriptor with { CommentRows = commentRows }, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutCommentRows()
        => WithCommentRows([]);
    public DialectDescriptorBuilder WithArray(Func<ArrayDialectDescriptorBuilder, ArrayDialectDescriptorBuilder> builder)
        => (ArrayBuilder = builder(ArrayBuilder ?? new()), Builder: this).Builder;

    public DialectDescriptor Build()
    {
        if (ArrayBuilder is not null)
        {
            var (delimiter, prefix, suffix) = ArrayBuilder.Build();
            Descriptor = Descriptor with
            {
                ArrayDelimiter = delimiter,
                ArrayPrefix = prefix,
                ArraySuffix = suffix
            };
        }
        return Descriptor;
    }
}
