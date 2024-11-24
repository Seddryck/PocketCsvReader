using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class LineTerminatorParser : IInternalCharParser
{
    public int Index { get; private set; } = 1;
    private readonly int _length;

    protected CharParser Parser { get; set; }

    public LineTerminatorParser(CharParser parser, int length)
        => (Parser, _length) = (parser, length);

    protected void Reset()
        => Index = 1;

    private bool IsLast()
        => Index == _length;

    public ParserState Parse(char c)
    {
        if (c == Parser.Profile.Descriptor.LineTerminator[Index])
        {
            Index++;
            if (IsLast())
            {
                if (Parser.Position < 0)
                    Parser.SetFieldEnd(Parser.Position);
                Parser.Switch(Parser.FirstCharOfRecord);
                Reset();
                return NextState();
            }
            return ParserState.Continue;
        }

        return SetBack();
    }

    public virtual ParserState NextState()
        => ParserState.Record;

    public virtual ParserState SetBack()
    {
        Reset();
        Parser.Switch(Parser.CharOfField);
        return ParserState.Continue;
    }
}
