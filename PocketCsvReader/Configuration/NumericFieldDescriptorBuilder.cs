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

    public NumericFieldDescriptorBuilder WithGroupChar(char groupChar)
    {
        _groupChar = groupChar;
        return this;
    }

    public override FieldDescriptor Build()
        => new NumericFieldDescriptor(_runtimeType, _name, _format, _decimalChar, _groupChar);
}
