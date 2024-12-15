using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class DialectDescriptorBuilder
{
    private CsvDialectDescriptor Descriptor { get; } = new();

    public DialectDescriptorBuilder WithDelimiter(char delimiter)
        => (Descriptor.Delimiter = delimiter, Builder: this).Builder;
    public DialectDescriptorBuilder WithDelimiter(Delimiter delimiter)
        => WithDelimiter((char)delimiter);
    public DialectDescriptorBuilder WithLineTerminator(string lineTerminator)
        => (Descriptor.LineTerminator = lineTerminator, Builder: this).Builder;
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
        => (Descriptor.QuoteChar = quoteChar, Builder: this).Builder;
    public DialectDescriptorBuilder WithQuoteChar(QuoteChar quoteChar)
        => WithQuoteChar((char)quoteChar);
    public DialectDescriptorBuilder WithDoubleQuote(bool doubleQuote = true)
        => (Descriptor.DoubleQuote = doubleQuote, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutDoubleQuote()
        => WithDoubleQuote(false);
    public DialectDescriptorBuilder WithEscapeChar(char escapeChar)
        => (Descriptor.EscapeChar = escapeChar, Builder: this).Builder;
    public DialectDescriptorBuilder WithEscapeChar(EscapeChar escapeChar)
        => WithEscapeChar((char)escapeChar);
    public DialectDescriptorBuilder WithNullSequence(string? nullSequence)
        => (Descriptor.NullSequence = nullSequence, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutNullSequence()
        => WithNullSequence(null);
    public DialectDescriptorBuilder WithSkipInitialSpace(bool skipInitialSpace = true)
        => (Descriptor.SkipInitialSpace = skipInitialSpace, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutSkipInitialSpace()
        => WithSkipInitialSpace(false);
    public DialectDescriptorBuilder WithHeader(bool header = true)
        => (Descriptor.Header = header, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutHeader()
        => WithHeader(false);
    public DialectDescriptorBuilder WithCommentChar(char commentChar)
        => (Descriptor.CommentChar = commentChar, Builder: this).Builder;
    public DialectDescriptorBuilder WithCommentChar(CommentChar commentChar)
       => WithCommentChar((char)commentChar);
    public DialectDescriptorBuilder WithCaseSensitiveHeader(bool caseSensitiveHeader = true)
        => (Descriptor.CaseSensitiveHeader = caseSensitiveHeader, Builder: this).Builder;
    public DialectDescriptorBuilder WithoutCaseSensitiveHeader()
        => WithCaseSensitiveHeader(false);
    public DialectDescriptorBuilder WithCsvDdfVersion(string version)
        => (Descriptor.CsvDdfVersion = version, Builder: this).Builder;

    public CsvDialectDescriptor Build()
        => Descriptor;
}
