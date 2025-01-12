using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PocketCsvReader.Configuration;
public class NumericFieldDescriptorBuilder : FieldDescriptorBuilder
{
    private char? _decimalChar;
    private char? _groupChar;

    internal NumericFieldDescriptorBuilder(Type runtimeType)
        : base(runtimeType) { }

    public NumericFieldDescriptorBuilder WithDecimalChar(char decimalChar)
    {
        _decimalChar = decimalChar;
        return this;
    }

    public NumericFieldDescriptorBuilder WithGroupChar(char? groupChar)
    {
        _groupChar = groupChar;
        return this;
    }

    public NumericFieldDescriptorBuilder WithoutGroupChar()
    {
        _groupChar = null;
        return this;
    }

    public new NumericFieldDescriptorBuilder WithName(string value)
        => (NumericFieldDescriptorBuilder)base.WithName(value);

    public new NumericFieldDescriptorBuilder WithFormat(string value)
        => (NumericFieldDescriptorBuilder)base.WithFormat(value);

    public new NumericFieldDescriptorBuilder WithSequence(string pattern, string? value)
        => (NumericFieldDescriptorBuilder)base.WithSequence(pattern, value);

    public new NumericFieldDescriptorBuilder WithDataSourceTypeName(string typeName)
        => (NumericFieldDescriptorBuilder)base.WithDataSourceTypeName(typeName);

    public override FieldDescriptor Build()
        => new NumericFieldDescriptor(_runtimeType, _name, _format, _sequences?.ToImmutable(), _dataSourceTypeName ?? string.Empty, _decimalChar, _groupChar);
}
