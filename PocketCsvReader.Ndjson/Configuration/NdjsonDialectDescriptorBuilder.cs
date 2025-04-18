using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Ndjson.Configuration;
public class NdjsonDialectDescriptorBuilder
{
    private NdjsonDialectDescriptor Descriptor { get; set; } = new();

    public NdjsonDialectDescriptorBuilder WithDelimiter(char delimiter)
        => (Descriptor = Descriptor with { Delimiter = delimiter }, Builder: this).Builder;
    public NdjsonDialectDescriptorBuilder WithDelimiter(Delimiter delimiter)
        => WithDelimiter((char)delimiter);
    public NdjsonDialectDescriptorBuilder WithSeparator(char separator)
        => (Descriptor = Descriptor with { Separator = separator }, Builder: this).Builder;
    public NdjsonDialectDescriptorBuilder WithSeparator(Delimiter separator)
        => WithSeparator((char)separator);
    public NdjsonDialectDescriptorBuilder WithLineTerminator(string lineTerminator)
        => (Descriptor = Descriptor with { LineTerminator = lineTerminator }, Builder: this).Builder;
    public NdjsonDialectDescriptorBuilder WithLineTerminator(LineTerminator lineTerminator)
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
    public NdjsonDialectDescriptorBuilder WithQuoteChar(char? quoteChar)
        => (Descriptor = Descriptor with { QuoteChar = quoteChar }, Builder: this).Builder;
    public NdjsonDialectDescriptorBuilder WithQuoteChar(QuoteChar quoteChar)
        => WithQuoteChar((char)quoteChar);
    public NdjsonDialectDescriptorBuilder WithoutQuoteChar()
        => (Descriptor = Descriptor with { QuoteChar = null }, Builder: this).Builder;
    public NdjsonDialectDescriptorBuilder WithEscapeChar(char? escapeChar)
        => (Descriptor = Descriptor with { EscapeChar = escapeChar}, Builder: this).Builder;
    public NdjsonDialectDescriptorBuilder WithEscapeChar(EscapeChar escapeChar)
        => WithEscapeChar((char)escapeChar);
    public NdjsonDialectDescriptorBuilder WithoutEscapeChar()
        => (Descriptor = Descriptor with { EscapeChar = null }, Builder: this).Builder;

    public NdjsonDialectDescriptor Build()
        => Descriptor;
}
