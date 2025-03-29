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

    public CustomFieldDescriptorBuilder WithFormat(string pattern, IFormatProvider? formatProvider = null)
    {
        format = new CustomFormatDescriptorBuilder(pattern, formatProvider);
        return this;
    }

    public new CustomFieldDescriptorBuilder WithName(string value)
        => (CustomFieldDescriptorBuilder)base.WithName(value);

    public new CustomFieldDescriptorBuilder WithSequence(string pattern, string? value)
        => (CustomFieldDescriptorBuilder)base.WithSequence(pattern, value);

    public new CustomFieldDescriptorBuilder WithDataSourceTypeName(string typeName)
        => (CustomFieldDescriptorBuilder)base.WithDataSourceTypeName(typeName);
    public new CustomFieldDescriptorBuilder WithParser(ParseFunction parse)
        => (CustomFieldDescriptorBuilder)base.WithParser(parse);
}
