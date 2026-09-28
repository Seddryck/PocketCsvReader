using PocketCsvReader.CharParsing;

namespace PocketCsvReader.Ndjson.CharParsing;

internal readonly struct CommentParser : IParser
{
    private readonly IParserContext _context;
    private readonly INdjsonStateController _controller;
    private readonly char _lineTerminatorChar;
    private readonly int _lineTerminatorLength;

    public CommentParser(IParserContext context, INdjsonStateController controller, string lineTerminator)
        => (_context, _controller, _lineTerminatorChar, _lineTerminatorLength) =
            (context, controller, lineTerminator[0], lineTerminator.Length);

    public ParserState Parse(char c, int pos)
    {
        if (c != _lineTerminatorChar)
            return ParserState.Continue;

        if (_lineTerminatorLength == 1)
        {
            _controller.SwitchToObjectPrefix();
            return ParserState.Comment;
        }

        _controller.SwitchToLineTerminator(ParserState.Comment);
        return ParserState.Continue;
    }

    public ParserState ParseEof(int pos)
        => ParserState.Eof;

    public void Reset()
    {
        _context.Reset();
        _controller.Reset();
    }

    public ref FieldSpan Result
        => ref _context.Span;
}
