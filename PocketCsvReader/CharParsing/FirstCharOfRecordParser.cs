using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class FirstCharOfRecordParser : FirstCharOfFieldParser
{
    private int[] CommentRows { get; set; }
    private int[] HeaderRows { get; set; }
    private char? CommentChar { get; set; }

    public FirstCharOfRecordParser(CharParser parser)
        : base(parser)
    {
        CommentChar = Parser.Profile.Dialect.CommentChar;
        CommentRows = Parser.Profile.Dialect.CommentRows ?? [];
        HeaderRows = Parser.Profile.Dialect.Header ? Parser.Profile.Dialect.HeaderRows : [];
    }

    public override ParserState Parse(char c)
    {
        Parser.RowNumber++;

        if (HeaderRows.Contains(Parser.RowNumber))
            Parser.SetHeaderRow();
        else if (CommentRows.Contains(Parser.RowNumber) || (CommentChar.HasValue && c == CommentChar))
        {
            Parser.UnsetHeaderRow();
            Parser.ZeroField();
            Parser.Switch(Parser.Comment);
            return ParserState.Continue;
        }
        else if (Parser.IsHeaderRow)
            Parser.UnsetHeaderRow();

        return base.Parse(c);
    }
}
