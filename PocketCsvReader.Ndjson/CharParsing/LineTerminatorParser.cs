using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.CharParsing;
internal class LineTerminatorParser : IInternalCharParser
{
    public int Index { get; private set; } = 0;
    private readonly int _length;

    protected CharParser Parser { get; set; }
    private char[] Whitespaces { get; }

    public LineTerminatorParser(CharParser parser, int length)
        => (Parser, _length, Whitespaces) = (parser, length, parser.Profile.Dialect.Whitespaces);

    protected void Reset()
        => Index = 0;

    private bool IsLast()
        => Index == _length;

    public ParserState Parse(char c)
    {
        if (c == Parser.Profile.Dialect.LineTerminator[Index])
        {
            Index++;
            if (IsLast())
            {
                Parser.Switch(Parser.FirstCharOfRecord);
                Reset();
                return ParserState.Continue;
            }
            return ParserState.Continue;
        }

        return ParserState.Error;
    }
}
