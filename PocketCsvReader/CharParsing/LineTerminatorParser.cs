using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
public class LineTerminatorParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    private readonly char[] _lineTerminators;
    private int _index = 0;
    private ParserState _returnState = ParserState.Record;

    /// <summary>
    /// Initializes a new instance of the <see cref="LineTerminatorParser"/> with the specified parser context, state controller, and line terminator sequence.
    /// </summary>
    /// <param name="ctx">The parser context providing access to parsing state and results.</param>
    /// <param name="controller">The state controller used to manage parser transitions.</param>
    /// <param name="lineTerminator">The line terminator sequence to match during parsing.</param>
    public LineTerminatorParser(IParserContext ctx, IParserStateController controller, string lineTerminator)
        => (_ctx, _controller, _lineTerminators)
            = (ctx, controller, lineTerminator.ToCharArray());

    /// <summary>
    /// Attempts to match the next character in the line terminator sequence. Returns the configured parser state if the full sequence is matched; otherwise, continues parsing or resets on mismatch.
    /// </summary>
    /// <param name="c">The current character to parse.</param>
    /// <param name="pos">The position of the character in the input stream.</param>
    /// <returns>The configured return state if the line terminator is fully matched; otherwise, <see cref="ParserState.Continue"/>.</returns>
    public ParserState Parse(char c, int pos)
    {
        if (c == _lineTerminators[++_index])
        {
            if (_index == _lineTerminators.Length - 1)
                return _returnState;
            return ParserState.Continue;
        }
        else
        {
            Reset();
            _controller.SwitchBack();
            return ParserState.Continue;
        }
    }

    /// <summary>
    /// Returns the parser's current return state when the end of input is reached.
    /// </summary>
    /// <param name="pos">The position in the input where EOF was encountered.</param>
    /// <returns>The parser state to transition to at EOF.</returns>
    public ParserState ParseEof(int pos)
    => _returnState;
    /// <summary>
    /// Resets the parser to its initial state, clearing progress through the line terminator sequence and setting the return state to <c>ParserState.Record</c>.
    /// </summary>
    public void Reset()
    {
        _index = 0;
        _returnState = ParserState.Record;
    }

    /// <summary>
    /// Sets the parser state to return after successfully parsing the line terminator sequence.
    /// </summary>
    /// <param name="state">The parser state to return upon completion.</param>
    public void ReturnState(ParserState state)
    {
        _returnState = state;
    }

    public ref FieldSpan Result
        => ref _ctx.Span;
}
