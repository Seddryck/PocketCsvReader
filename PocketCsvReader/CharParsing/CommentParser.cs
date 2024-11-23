using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class CommentParser : LineTerminatorParser
{
    public CommentParser(CharParser parser, int length)
        : base(parser, length) { }

    public override ParserState NextState()
        => ParserState.Continue;

    public override ParserState SetBack()
    {
        Reset();
        Parser.Switch(Parser.Comment);
        return ParserState.Continue;
    }
}
