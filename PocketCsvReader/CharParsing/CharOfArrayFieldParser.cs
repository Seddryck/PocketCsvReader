using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class CharOfArrayFieldParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }
    private char? ArraySuffix { get; set; }
    private CharParser ArrayParser { get; set; }

    public CharOfArrayFieldParser(CharParser parser)
    {
        Parser = parser;
        ArraySuffix = parser.Profile.Dialect.ArraySuffix;
        if (!parser.Profile.Dialect.ArrayDelimiter.HasValue)
            throw new InvalidOperationException("Array delimiter must be specified to parse array fields.");
        var dialect = parser.Profile.Dialect with
        {
            Delimiter = parser.Profile.Dialect.ArrayDelimiter.Value,
            ArrayDelimiter = null,
            ArrayPrefix = null,
            ArraySuffix = null
        };
        ArrayParser = new CharParser(new CsvProfile(dialect));
    }

    public virtual ParserState Parse(char c)
    {
        if (ArraySuffix.HasValue && c == ArraySuffix.Value)
        {
            ArrayParser.SetFieldEnd();
            Parser.AddChild(CreateFieldSpan());
            Parser.Switch(Parser.AfterArray);
        }
        else
        {
            var childState = ArrayParser.Parse(c);
            if (childState == ParserState.Field)
                Parser.AddChild(CreateFieldSpan());
            if (childState == ParserState.Error)
                return ParserState.Error;
        }
        return ParserState.Continue;
    }

    private FieldSpan CreateFieldSpan()
        => new FieldSpan(ArrayParser.ValueStart, ArrayParser.ValueLength,
                    ArrayParser.IsQuotedField, ArrayParser.IsEscapedField,
                    ArrayParser.LabelStart, ArrayParser.LabelLength, null);
}
