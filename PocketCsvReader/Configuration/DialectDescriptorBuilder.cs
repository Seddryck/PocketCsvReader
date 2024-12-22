using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class DialectDescriptorBuilder
{
    private DialectDescriptor Descriptor { get; set; } = new();

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
    public DialectDescriptorBuilder WithQuoteChar(char quoteChar)
        => (Descriptor = Descriptor with { QuoteChar = quoteChar }, Builder: this).Builder;
    public DialectDescriptorBuilder WithQuoteChar(QuoteChar quoteChar)
        => WithQuoteChar((char)quoteChar);
    public DialectDescriptorBuilder WithDoubleQuote(bool doubleQuote = true)
        => (Descriptor = Descriptor with { DoubleQuote = doubleQuote }, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutDoubleQuote()
        => WithDoubleQuote(false);
    public DialectDescriptorBuilder WithEscapeChar(char escapeChar)
        => (Descriptor = Descriptor with { EscapeChar = escapeChar}, Builder: this).Builder;
    public DialectDescriptorBuilder WithEscapeChar(EscapeChar escapeChar)
        => WithEscapeChar((char)escapeChar);
    public DialectDescriptorBuilder WithNullSequence(string? nullSequence)
        => (Descriptor = Descriptor with { NullSequence = nullSequence}, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutNullSequence()
        => WithNullSequence(null);
    public DialectDescriptorBuilder WithSkipInitialSpace(bool skipInitialSpace = true)
        => (Descriptor = Descriptor with { SkipInitialSpace = skipInitialSpace}, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutSkipInitialSpace()
        => WithSkipInitialSpace(false);
    public DialectDescriptorBuilder WithHeader(bool header = true)
        => (Descriptor = Descriptor with { Header = header}, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutHeader()
        => WithHeader(false);
    public DialectDescriptorBuilder WithCommentChar(char commentChar)
        => (Descriptor = Descriptor with { CommentChar = commentChar}, Builder: this).Builder;
    public DialectDescriptorBuilder WithCommentChar(CommentChar commentChar)
       => WithCommentChar((char)commentChar);
    public DialectDescriptorBuilder WithCsvDdfVersion(string version)
        => (Descriptor = Descriptor with { CsvDdfVersion = version}, Builder: this).Builder;

    public DialectDescriptor Build()
        => Descriptor;
}
