using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Ndjson.Configuration;
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
    public DialectDescriptorBuilder WithQuoteChar(char? quoteChar)
        => (Descriptor = Descriptor with { QuoteChar = quoteChar }, Builder: this).Builder;
    public DialectDescriptorBuilder WithQuoteChar(QuoteChar quoteChar)
        => WithQuoteChar((char)quoteChar);
    public DialectDescriptorBuilder WithoutQuoteChar()
        => (Descriptor = Descriptor with { QuoteChar = null }, Builder: this).Builder;
    public DialectDescriptorBuilder WithEscapeChar(char? escapeChar)
        => (Descriptor = Descriptor with { EscapeChar = escapeChar}, Builder: this).Builder;
    public DialectDescriptorBuilder WithEscapeChar(EscapeChar escapeChar)
        => WithEscapeChar((char)escapeChar);
    public DialectDescriptorBuilder WithoutEscapeChar()
        => (Descriptor = Descriptor with { EscapeChar = null }, Builder: this).Builder;

    public DialectDescriptor Build()
        => Descriptor;
}
