using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PocketCsvReader.Configuration;
public class NumberFieldDescriptorBuilder : FieldDescriptorBuilder
{
    internal NumberFieldDescriptorBuilder(Type runtimeType)
        : base(runtimeType) { }

    public NumberFieldDescriptorBuilder WithFormat(Func<NumberFormatDescriptorBuilder, NumberFormatDescriptorBuilder> func)
    {
        format = func(new NumberFormatDescriptorBuilder());
        return this;
    }

    public new NumberFieldDescriptorBuilder WithName(string value)
        => (NumberFieldDescriptorBuilder)base.WithName(value);

    public new NumberFieldDescriptorBuilder WithSequence(string pattern, string? value)
        => (NumberFieldDescriptorBuilder)base.WithSequence(pattern, value);

    public new NumberFieldDescriptorBuilder WithDataSourceTypeName(string typeName)
        => (NumberFieldDescriptorBuilder)base.WithDataSourceTypeName(typeName);
    public new NumberFieldDescriptorBuilder WithParser(ParseFunction parse)
        => (NumberFieldDescriptorBuilder)base.WithParser(parse);

    public override FieldDescriptor Build()
        => new FieldDescriptor(runtimeType, name, format?.Build(), parse, sequences?.ToImmutable(), dataSourceTypeName ?? string.Empty);
}
