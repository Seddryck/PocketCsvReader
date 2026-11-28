using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class CustomFieldDescriptorBuilder : FieldDescriptorBuilder
{
    internal CustomFieldDescriptorBuilder(Type runtimeType)
        : base(runtimeType) { }

    public new CustomFieldDescriptorBuilder WithFormat(string pattern, IFormatProvider? formatProvider = null)
        => (CustomFieldDescriptorBuilder)base.WithFormat(pattern, formatProvider);

    public new CustomFieldDescriptorBuilder WithName(string value)
        => (CustomFieldDescriptorBuilder)base.WithName(value);

    public new CustomFieldDescriptorBuilder WithSequence(string pattern, string? value)
        => (CustomFieldDescriptorBuilder)base.WithSequence(pattern, value);

    /// <inheritdoc />
    public new CustomFieldDescriptorBuilder WithRuntimeNullDefault(object value)
        => (CustomFieldDescriptorBuilder)base.WithRuntimeNullDefault(value);

    public new CustomFieldDescriptorBuilder WithDataSourceTypeName(string typeName)
        => (CustomFieldDescriptorBuilder)base.WithDataSourceTypeName(typeName);
    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public new CustomFieldDescriptorBuilder WithParser<T>(ParseSpan<T> parse)
        => (CustomFieldDescriptorBuilder)base.WithParser(parse);

    public new CustomFieldDescriptorBuilder WithParser(ParseFunction parse)
        => (CustomFieldDescriptorBuilder)base.WithParser(parse);
}
