using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class AfterEscapeCharQuotedFieldParser : CharOfQuotedFieldParser
{
    public AfterEscapeCharQuotedFieldParser(CharParser parser)
        : base(parser) { }

    public override ParserState Parse(char c)
    {
        if (c == Parser.Profile.Dialect.QuoteChar
            || c == Parser.Profile.Dialect.EscapeChar)
        {
            Parser.SetEscapedField();
            Parser.Switch(Parser.CharOfQuotedField);
            return ParserState.Continue;
        }

        return base.Parse(c);
    }
}
