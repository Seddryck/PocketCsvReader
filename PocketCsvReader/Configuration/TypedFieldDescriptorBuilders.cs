using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace PocketCsvReader.Configuration;

/// <summary>Configures a schema field and preserves fluent chaining.</summary>
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
    {
        var configured = (configure?.Invoke(Self) ?? Self).UntypedBuilder;
        if (configured.RuntimeType != UntypedBuilder.RuntimeType)
            throw new InvalidOperationException("Field configuration must preserve the declared field type.");
        return configured;
    }

    internal TBuilder SetNullDefault(object value)
    {
        UntypedBuilder.SetNullDefault(value);
        return Self;
    }
}

public sealed class FieldDescriptorBuilder<TField> : FieldDescriptorBuilder<TField, FieldDescriptorBuilder<TField>>
{
    internal FieldDescriptorBuilder()
        : this(new FieldDescriptorBuilder(typeof(TField))) { }

    internal FieldDescriptorBuilder(FieldDescriptorBuilder builder)
        : base(builder) { }
}

public sealed class IntegerFieldDescriptorBuilder<TField> : FieldDescriptorBuilder<TField, IntegerFieldDescriptorBuilder<TField>>
{
    internal IntegerFieldDescriptorBuilder()
        : this(new IntegerFieldDescriptorBuilder(typeof(TField))) { }

    internal IntegerFieldDescriptorBuilder(IntegerFieldDescriptorBuilder builder)
        : base(builder) { }

    public IntegerFieldDescriptorBuilder<TField> WithFormat(Func<IntegerFormatDescriptorBuilder, IntegerFormatDescriptorBuilder> func)
    {
        ((IntegerFieldDescriptorBuilder)UntypedBuilder).WithFormat(func);
        return this;
    }
}

public sealed class NumberFieldDescriptorBuilder<TField> : FieldDescriptorBuilder<TField, NumberFieldDescriptorBuilder<TField>>
{
    internal NumberFieldDescriptorBuilder()
        : this(new NumberFieldDescriptorBuilder(typeof(TField))) { }

    internal NumberFieldDescriptorBuilder(NumberFieldDescriptorBuilder builder)
        : base(builder) { }

    public NumberFieldDescriptorBuilder<TField> WithFormat(Func<NumberFormatDescriptorBuilder, NumberFormatDescriptorBuilder> func)
    {
        ((NumberFieldDescriptorBuilder)UntypedBuilder).WithFormat(func);
        return this;
    }
}

public sealed class TemporalFieldDescriptorBuilder<TField> : FieldDescriptorBuilder<TField, TemporalFieldDescriptorBuilder<TField>>
{
    internal TemporalFieldDescriptorBuilder()
        : this(new TemporalFieldDescriptorBuilder(typeof(TField))) { }

    internal TemporalFieldDescriptorBuilder(TemporalFieldDescriptorBuilder builder)
        : base(builder) { }

    public TemporalFieldDescriptorBuilder<TField> WithFormat(string pattern, Func<TemporalFormatDescriptorBuilder, TemporalFormatDescriptorBuilder>? func = null)
    {
        ((TemporalFieldDescriptorBuilder)UntypedBuilder).WithFormat(pattern, func);
        return this;
    }
}

/// <summary>Configures non-null defaults for schema fields.</summary>
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

    /// <summary>Supplies a non-null default for a reference field or a field declared with a runtime type.</summary>
    // Generic constraints alone cannot distinguish this signature from the value-type overload.
    public static TBuilder WithNullDefault<TValue, TBuilder>(this FieldDescriptorBuilder<TValue, TBuilder> builder, [DisallowNull] in TValue value)
        where TValue : class?
        where TBuilder : FieldDescriptorBuilder<TValue, TBuilder>
        => builder.SetNullDefault(value!);
}
