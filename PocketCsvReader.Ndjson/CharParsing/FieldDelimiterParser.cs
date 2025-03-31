using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.CharParsing;
internal class FieldDelimiterParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }

    private char? Delimiter { get; set; }
    private char? EndRecord { get; }
    private char[] Whitespaces { get; }

    public FieldDelimiterParser(CharParser parser)
        => (Parser, Delimiter, EndRecord, Whitespaces)
                = (parser,
                    parser.Profile.Dialect.Delimiter,
                    parser.Profile.Dialect.EndRecord,
                    parser.Profile.Dialect.Whitespaces);

    public ParserState Parse(char c)
    {
        if (Delimiter.HasValue && c == Delimiter.Value)
        {
            Parser.Switch(Parser.QuotedCharOfLabel);
            return ParserState.Field;
        }
        else if (EndRecord.HasValue && c == EndRecord.Value)
        {
            Parser.Switch(Parser.LineTerminator);
            return ParserState.Field;
        }
        else if (Whitespaces.Contains(c))
            return ParserState.Continue;

        return ParserState.Error;
    }
}
