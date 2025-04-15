using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

class FieldContext : IParserContext
{
    private FieldSpan _span;
    private bool _escaping;
    private bool _doubling;
    private bool _escaped;
    private bool _complete;

    public IParserContext? Parent { get; } 

    /// <summary>
    /// Initializes a new instance of the <see cref="FieldContext"/> class with default parsing state.
    /// </summary>
    public FieldContext()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="FieldContext"/> class with the specified parent parser context.
    /// </summary>
    /// <param name="parent">The parent parsing context to associate with this field context.</param>
    public FieldContext(IParserContext parent)
    {
        Parent = parent;
    }

    public ref FieldSpan Span => ref _span;
    public bool IsComplete => _complete;

    /// <summary>
        /// Marks the start position of the label segment within the field span.
        /// </summary>
        /// <param name="pos">The character index where the label starts.</param>
        /// <param name="quoted">Indicates whether the label is quoted. (This parameter is currently unused.)</param>
        public void StartLabel(int pos, bool quoted)
        => _span.Label = _span.Label with { Start = pos };

    /// <summary>
        /// Marks the end of the label segment by setting its length based on the specified position.
        /// </summary>
        /// <param name="pos">The position indicating the end of the label segment.</param>
        public void EndLabel(int pos)
        => _span.Label = _span.Label with { Length = pos - _span.Label.Start };

    /// <summary>
    /// Marks the start position of the field value, adjusting for quoted values.
    /// </summary>
    /// <param name="pos">The position in the input where the value starts.</param>
    /// <param name="quoted">Indicates whether the value is quoted; if true, the start position is incremented to skip the opening quote.</param>
    public void StartValue(int pos, bool quoted)
    {
        _span.Value = _span.Value with { Start = quoted ? pos + 1 : pos, WasQuoted = quoted, IsStarted = true };
    }

    /// <summary>
    /// Marks the end of the field value at the specified position and sets the field as complete.
    /// </summary>
    /// <param name="pos">The position where the value segment ends.</param>
    public void EndValue(int pos)
    {
        _span.Value = _span.Value with { Length = pos - _span.Value.Start + 1 };
        _complete = true;
    }

    /// <summary>
    /// Marks the field as having an empty value and sets its parsing state to complete.
    /// </summary>
    public void EmptyValue()
    {
        _span.Value = _span.Value with { Length = 0 };
        _complete = true;
    }

    /// <summary>
    /// Marks the beginning of an escape sequence during field parsing.
    /// </summary>
    public void StartEscaping()
    {
        _escaping = true;
    }

    /// <summary>
    /// Marks the end of an escape sequence for the current field value, updates the escaped state, and resets escaping and completion flags.
    /// </summary>
    public void EndEscaping()
    {
        _escaped = true;
        _span.Value = _span.Value with { IsEscaped = true };
        _escaping = false;
        _complete = false;
    }

    /// <summary>
    /// Clears the escaping state for the current field context.
    /// </summary>
    public void RemoveEscaping()
    {
        _escaping = false;
    }

    /// <summary>
    /// Marks the beginning of a doubled character sequence during field parsing.
    /// </summary>
    public void StartDoubling()
    {
        _doubling = true;
    }

    /// <summary>
    /// Marks the end of a doubling sequence in the field, sets the field as escaped, and updates the parsing state accordingly.
    /// </summary>
    public void EndDoubling()
    {
        _escaped = true;
        _span.Value = _span.Value with { IsEscaped = true };
        _doubling = false;
        _complete = false;
    }

    /// <summary>
    /// Clears the doubling state, indicating that the parser is no longer processing a doubled character.
    /// </summary>
    public void RemoveDoubling()
    {
        _doubling = false;
    }

    public bool Escaping => _escaping;
    public bool Doubling => _doubling;
    public bool Escaped => _escaped;

    /// <summary>
        /// Adds a child <see cref="FieldSpan"/> to the current field's collection of child spans.
        /// </summary>
        /// <param name="child">The child span to add.</param>
        public void AddChild(FieldSpan child)
        => Span.Children = Span.Children?.Append(child).ToArray() ?? [child];

    /// <summary>
    /// Resets the field context to its initial state, clearing all parsing state and metadata.
    /// </summary>
    public void Reset()
    {
        _span = default;
        _escaping = false;
        _escaped = false;
        _doubling = false;
        _complete = false;
    }
}
