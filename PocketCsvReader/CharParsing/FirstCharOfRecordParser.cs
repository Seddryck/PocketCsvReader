using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class FirstCharOfRecordParser : FirstCharOfFieldParser
{
    public FirstCharOfRecordParser(CharParser parser)
        : base(parser) { }

    public override ParserState Parse(char c)
    {
        if (c == Parser.Profile.Descriptor.CommentChar)
        {
            Parser.ZeroField();
            Parser.Switch(Parser.Comment);
            return ParserState.Continue;
        }

        return base.Parse(c);
    }
}
