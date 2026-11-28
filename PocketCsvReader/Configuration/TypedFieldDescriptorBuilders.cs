using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace PocketCsvReader.Configuration;

/// <summary>Configures a field whose type is known at compile time.</summary>
public abstract class FieldDescriptorBuilder<TField, TBuilder>
    where TBuilder : FieldDescriptorBuilder<TField, TBuilder>
{
    private protected FieldDescriptorBuilder(FieldDescriptorBuilder builder) => UntypedBuilder = builder;

    private protected FieldDescriptorBuilder UntypedBuilder { get; }

    private TBuilder Self => (TBuilder)this;

    public TBuilder WithName(string name)
    {
        UntypedBuilder.WithName(name);
        return Self;
    }

    public TBuilder WithSequence(string pattern, string? value)
    {
        UntypedBuilder.WithSequence(pattern, value);
        return Self;
    }

    public TBuilder WithDataSourceTypeName(string typeName)
    {
        UntypedBuilder.WithDataSourceTypeName(typeName);
        return Self;
    }

    public TBuilder WithFormat(string pattern, IFormatProvider? formatProvider = null)
    {
        UntypedBuilder.WithFormat(pattern, formatProvider);
        return Self;
    }

    public TBuilder WithParser(ParseFunction parse)
    {
        UntypedBuilder.WithParser(parse);
        return Self;
    }

    [OverloadResolutionPriority(-1)]
    public TBuilder WithParser<T>(ParseSpan<T> parse)
    {
        UntypedBuilder.WithParser(parse);
        return Self;
    }

    public TBuilder FromHex()
    {
        UntypedBuilder.FromHex();
        return Self;
    }

    public TBuilder FromBase64()
    {
        UntypedBuilder.FromBase64();
        return Self;
    }

    public TBuilder FromBase64Url()
    {
        UntypedBuilder.FromBase64Url();
        return Self;
    }

    public TBuilder FromBase32()
    {
        UntypedBuilder.FromBase32();
        return Self;
    }

    public TBuilder FromBase32Hex()
    {
        UntypedBuilder.FromBase32Hex();
        return Self;
    }

    public FieldDescriptor Build() => UntypedBuilder.Build();

    internal FieldDescriptorBuilder Configure(Func<TBuilder, TBuilder>? configure)
        => (configure?.Invoke(Self) ?? Self).UntypedBuilder;

    internal TBuilder SetNullDefault(object value)
    {
        UntypedBuilder.WithRuntimeNullDefault(value);
        return Self;
    }
}

public sealed class FieldDescriptorBuilder<TField> : FieldDescriptorBuilder<TField, FieldDescriptorBuilder<TField>>
{
    internal FieldDescriptorBuilder()
        : base(new FieldDescriptorBuilder(typeof(TField))) { }
}

public sealed class IntegerFieldDescriptorBuilder<TField> : FieldDescriptorBuilder<TField, IntegerFieldDescriptorBuilder<TField>>
{
    internal IntegerFieldDescriptorBuilder()
        : base(new IntegerFieldDescriptorBuilder(typeof(TField))) { }

    public IntegerFieldDescriptorBuilder<TField> WithFormat(Func<IntegerFormatDescriptorBuilder, IntegerFormatDescriptorBuilder> func)
    {
        ((IntegerFieldDescriptorBuilder)UntypedBuilder).WithFormat(func);
        return this;
    }
}

public sealed class NumberFieldDescriptorBuilder<TField> : FieldDescriptorBuilder<TField, NumberFieldDescriptorBuilder<TField>>
{
    internal NumberFieldDescriptorBuilder()
        : base(new NumberFieldDescriptorBuilder(typeof(TField))) { }

    public NumberFieldDescriptorBuilder<TField> WithFormat(Func<NumberFormatDescriptorBuilder, NumberFormatDescriptorBuilder> func)
    {
        ((NumberFieldDescriptorBuilder)UntypedBuilder).WithFormat(func);
        return this;
    }
}

public sealed class TemporalFieldDescriptorBuilder<TField> : FieldDescriptorBuilder<TField, TemporalFieldDescriptorBuilder<TField>>
{
    internal TemporalFieldDescriptorBuilder()
        : base(new TemporalFieldDescriptorBuilder(typeof(TField))) { }

    public TemporalFieldDescriptorBuilder<TField> WithFormat(string pattern, Func<TemporalFormatDescriptorBuilder, TemporalFormatDescriptorBuilder>? func = null)
    {
        ((TemporalFieldDescriptorBuilder)UntypedBuilder).WithFormat(pattern, func);
        return this;
    }
}

/// <summary>Configures non-null defaults for fields whose types are known at compile time.</summary>
public static class FieldNullDefaultExtensions
{
    /// <summary>Supplies a value to return when a value-type field is recognized as null.</summary>
    public static TBuilder WithNullDefault<TValue, TBuilder>(this FieldDescriptorBuilder<TValue, TBuilder> builder, TValue value)
        where TValue : struct
        where TBuilder : FieldDescriptorBuilder<TValue, TBuilder>
        => builder.SetNullDefault(value);

    /// <summary>Supplies an underlying value to return when a nullable value-type field is recognized as null.</summary>
    public static TBuilder WithNullDefault<TValue, TBuilder>(this FieldDescriptorBuilder<TValue?, TBuilder> builder, TValue value)
        where TValue : struct
        where TBuilder : FieldDescriptorBuilder<TValue?, TBuilder>
        => builder.SetNullDefault(value);

    /// <summary>Supplies a non-null reference to return when a reference-type field is recognized as null.</summary>
    // Generic constraints alone cannot distinguish this signature from the value-type overload.
    public static TBuilder WithNullDefault<TValue, TBuilder>(this FieldDescriptorBuilder<TValue, TBuilder> builder, [DisallowNull] in TValue value)
        where TValue : class?
        where TBuilder : FieldDescriptorBuilder<TValue, TBuilder>
        => builder.SetNullDefault(value!);
}
