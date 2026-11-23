using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;

public class FieldDescriptorBuilder
{
    private readonly Dictionary<Type, FormatDescriptorBuilder> defaultFormatBuilders = [];
    protected Type runtimeType;
    protected FormatDescriptorBuilder? Format { get; set; }
    protected ParseFunction? parse;
    protected Delegate? spanParse;
    protected string? name;
    protected SequenceCollection? sequences;
    protected string? dataSourceTypeName;

    protected internal FieldDescriptorBuilder(Type runtimeType)
    {
        this.runtimeType = runtimeType;
        Initialize();
    }

    private void Initialize()
    {
        defaultFormatBuilders.Add(typeof(short), new IntegerFormatDescriptorBuilder());
        defaultFormatBuilders.Add(typeof(int), new IntegerFormatDescriptorBuilder());
        defaultFormatBuilders.Add(typeof(long), new IntegerFormatDescriptorBuilder());

        defaultFormatBuilders.Add(typeof(float), new NumberFormatDescriptorBuilder());
        defaultFormatBuilders.Add(typeof(double), new NumberFormatDescriptorBuilder());
        defaultFormatBuilders.Add(typeof(decimal), new NumberFormatDescriptorBuilder());

        defaultFormatBuilders.Add(typeof(DateTime), new TemporalFormatDescriptorBuilder("yyyy-MM-ddTHH:mm:ss"));
        defaultFormatBuilders.Add(typeof(DateTimeOffset), new TemporalFormatDescriptorBuilder("yyyy-MM-ddTHH:mm:ss"));
        defaultFormatBuilders.Add(typeof(DateOnly), new TemporalFormatDescriptorBuilder("yyyy-MM-dd"));
        defaultFormatBuilders.Add(typeof(TimeOnly), new TemporalFormatDescriptorBuilder("yyyy-MM-ddTHH:mm:ss zzz"));
    }

    public FieldDescriptorBuilder WithName(string name)
    {
        this.name = name;
        return this;
    }

    public FieldDescriptorBuilder WithSequence(string pattern, string? value)
    {
        (sequences ??= []).Add(pattern, value);
        return this;
    }

    public FieldDescriptorBuilder WithDataSourceTypeName(string typeName)
    {
        dataSourceTypeName = typeName;
        return this;
    }

    /// <summary>Supplies a format pattern and optional format provider to a parser that supports them.</summary>
    public FieldDescriptorBuilder WithFormat(string pattern, IFormatProvider? formatProvider = null)
    {
        Format = new CustomFormatDescriptorBuilder(pattern, formatProvider);
        return this;
    }

    public FieldDescriptorBuilder WithParser(ParseFunction parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        this.parse = parse;
        spanParse = null;
        return this;
    }

    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public FieldDescriptorBuilder WithParser<T>(ParseSpan<T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        if (runtimeType != typeof(T))
            throw new ArgumentException($"Parser must return {runtimeType}.", nameof(parse));
        spanParse = parse;
        this.parse = null;
        return this;
    }

    /// <summary>Decodes hexadecimal text into a byte array.</summary>
    /// <exception cref="InvalidOperationException">The field type is not <c>byte[]</c>.</exception>
    public FieldDescriptorBuilder FromHex()
        => WithBinaryParser(Convert.FromHexString);

    /// <summary>Decodes Base64 text into a byte array.</summary>
    /// <exception cref="InvalidOperationException">The field type is not <c>byte[]</c>.</exception>
    public FieldDescriptorBuilder FromBase64()
        => WithBinaryParser(FieldParsing.Base64Decoder.Decode);

    /// <summary>Decodes RFC 4648 Base32 text into a byte array.</summary>
    /// <exception cref="InvalidOperationException">The field type is not <c>byte[]</c>.</exception>
    public FieldDescriptorBuilder FromBase32()
        => WithBinaryParser(FieldParsing.Base32Decoder.DecodeBase32);

    /// <summary>Decodes RFC 4648 Base32hex text into a byte array.</summary>
    /// <exception cref="InvalidOperationException">The field type is not <c>byte[]</c>.</exception>
    public FieldDescriptorBuilder FromBase32Hex()
        => WithBinaryParser(FieldParsing.Base32Decoder.DecodeBase32Hex);

    private FieldDescriptorBuilder WithBinaryParser(ParseSpan<byte[]> parse)
    {
        if (runtimeType != typeof(byte[]))
            throw new InvalidOperationException("A binary decoder requires a field of type byte[].");

        return WithParser<byte[]>(parse);
    }

    private FormatDescriptorBuilder GetDefaultFormat()
    {
        if (defaultFormatBuilders.TryGetValue(runtimeType, out var builder))
            return builder;
        return FormatDescriptorBuilder.None;
    }

    public virtual FieldDescriptor Build()
    {
        return new FieldDescriptor(runtimeType, name, (Format ?? GetDefaultFormat()).Build(), parse, sequences?.ToImmutable(), dataSourceTypeName ?? string.Empty) { SpanParse = spanParse };
    }
}
