using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class FirstCharOfRecordParser : FirstCharOfFieldParser
{
    private int[] CommentRows { get; set; }
    private char? CommentChar { get; set; }

    public FirstCharOfRecordParser(CharParser parser)
        : base(parser)
    {
        CommentChar = Parser.Profile.Descriptor.CommentChar;
        CommentRows = Parser.Profile.Descriptor.CommentRows ?? [];
    }

    public override ParserState Parse(char c)
    {
        if (CommentRows.Contains(++Parser.RowNumber) || (CommentChar.HasValue && c == CommentChar))
        {
            Parser.ZeroField();
            Parser.Switch(Parser.Comment);
            return ParserState.Continue;
        }

        return base.Parse(c);
    }
}
