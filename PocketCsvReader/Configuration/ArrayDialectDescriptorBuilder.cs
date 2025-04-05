using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class ArrayDialectDescriptorBuilder
{
    private char? Prefix { get; set; } = null;
    private char? Suffix { get; set; } = null;
    private char? Delimiter { get; set; } = null;

    public ArrayDialectDescriptorBuilder WithDelimiter(char delimiter)
        => (Delimiter = delimiter, Builder: this).Builder;
    public ArrayDialectDescriptorBuilder WithDelimiter(Delimiter delimiter)
        => (Delimiter = (char)delimiter, Builder: this).Builder;
    public ArrayDialectDescriptorBuilder WithPrefix(char prefix)
        => (Prefix = prefix, Builder: this).Builder;
    public ArrayDialectDescriptorBuilder WithSuffix(char suffix)
        => (Suffix = suffix, Builder: this).Builder;
    public ArrayDialectDescriptorBuilder WithoutPrefixAndSuffix()
        => (Prefix = null, Suffix = null, Builder: this).Builder;

    public (char delimiter, char? prefix, char? suffix) Build()
    {
        if (Delimiter is null)
            throw new InvalidOperationException("Delimiter cannot be null.");
        if ((Prefix is null && Suffix is not null) || (Prefix is not null && Suffix is null))
            throw new InvalidOperationException("Prefix and suffix must be both set or not set.");
        return (Delimiter.Value, Prefix, Suffix);
    }
}
