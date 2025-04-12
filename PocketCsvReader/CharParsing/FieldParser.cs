using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

public delegate ParserState ParserStateFn(char c, int pos);
class FieldParser : IParser
{
    public IParserContext Context { get; }
    public IParserStateController Controller { get; }

    public FieldParser(DialectDescriptor dialect)
        : this(null, null, dialect)
    { }

    protected internal FieldParser(IParserContext? ctx, IParserStateController? controller, DialectDescriptor dialect)
    {
        Context = ctx ?? new FieldContext();
        Controller = controller ?? new FieldStateController(Context, dialect);
    }

    public ParserState Parse(char c, int pos)
        => Controller.Parse(c, pos);

    public ParserState ParseEof(int pos)
        => Controller.ParseEof(pos);

    public void Reset()
    {
        Context.Reset();
        Controller.Reset();
    }
}
