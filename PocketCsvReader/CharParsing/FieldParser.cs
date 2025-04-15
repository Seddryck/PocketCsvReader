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

    /// <summary>
    /// Initializes a new <see cref="FieldParser"/> using the specified CSV dialect with default context and controller.
    /// </summary>
    /// <param name="dialect">The CSV dialect descriptor to configure parsing behavior.</param>
    public FieldParser(DialectDescriptor dialect)
        : this(null, null, dialect)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="FieldParser"/> class with optional custom context and controller, using the specified CSV dialect.
    /// </summary>
    /// <param name="ctx">Optional parser context. If null, a default <see cref="FieldContext"/> is used.</param>
    /// <param name="controller">Optional parser state controller. If null, a default <see cref="FieldStateController"/> is created with the provided context and dialect.</param>
    /// <param name="dialect">The CSV dialect descriptor used for parsing configuration.</param>
    protected internal FieldParser(IParserContext? ctx, IParserStateController? controller, DialectDescriptor dialect)
    {
        Context = ctx ?? new FieldContext();
        Controller = controller ?? new FieldStateController(Context, dialect);
    }

    /// <summary>
        /// Parses a single character at the specified position using the current parsing state.
        /// </summary>
        /// <param name="c">The character to parse.</param>
        /// <param name="pos">The position of the character in the input.</param>
        /// <returns>The resulting parser state after processing the character.</returns>
        public ParserState Parse(char c, int pos)
        => Controller.Parse(c, pos);

    /// <summary>
        /// Handles end-of-file parsing logic at the specified position.
        /// </summary>
        /// <param name="pos">The position in the input where EOF is encountered.</param>
        /// <returns>The resulting parser state after processing EOF.</returns>
        public ParserState ParseEof(int pos)
        => Controller.ParseEof(pos);

    /// <summary>
    /// Resets the parser context and controller to their initial states.
    /// </summary>
    public void Reset()
    {
        Context.Reset();
        Controller.Reset();
    }

    public ref FieldSpan Result
        => ref Context.Span;
}
