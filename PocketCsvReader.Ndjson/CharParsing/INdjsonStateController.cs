using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

public interface INdjsonStateController
{
    /// <summary>
    /// Parses a single character at the specified position using the current parser state.
    /// </summary>
    /// <param name="c">The character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>The resulting parser state after processing the character.</returns>
    public ParserState Parse(char c, int pos);

    /// <summary>
    /// Handles end-of-file parsing by delegating to the current parser.
    /// </summary>
    /// <param name="pos">The position in the input where EOF is encountered.</param>
    /// <returns>The resulting parser state after processing EOF.</returns>
    public ParserState ParseEof(int pos);

    public void SwitchToObjectPrefix();

    /// <summary>
    /// Switches the active parser to the value parser for standard CSV field content.
    /// </summary>
    public void SwitchToLabel();

    /// <summary>
    /// Switches the active parser to the quoted field parser.
    /// </summary>
    public void SwitchToLabelQuoted();

    /// <summary>
    /// Switches the active parser to the raw field parser for handling unquoted field content.
    /// </summary>
    public void SwitchToLabelRaw();

    /// <summary>
    /// Switches the active parser to the value parser for standard CSV field content.
    /// </summary>
    public void SwitchToSeparator();

    /// <summary>
    /// Switches the active parser to the value parser for standard CSV field content.
    /// </summary>
    public void SwitchToValue();

    /// <summary>
    /// Switches the active parser to the quoted field parser.
    /// </summary>
    public void SwitchToValueQuoted();

    /// <summary>
    /// Switches the active parser to the raw field parser for handling unquoted field content.
    /// </summary>
    public void SwitchToValueRaw();

    /// <summary>
    /// Switches the active parser to the array parser.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the array parser is not available for the current dialect.
    /// </exception>
    public void SwitchToArray();

    /// <summary>
    /// Switches the active parser to the comment parser. Throws an InvalidOperationException if comment parsing is not supported by the current dialect.
    /// </summary>
    public void SwitchToComment();

    /// <summary>
    /// Switches parsing to the line terminator parser, saving the current parser for later restoration and setting the return state after line termination.
    /// </summary>
    /// <param name="state">The parser state to return to after processing the line terminator.</param>
    public void SwitchToLineTerminator(ParserState state);

    /// <summary>
    /// Resets the controller and its parsers to the initial value parsing state.
    /// </summary>
    public void Reset();

    /// <summary>
    /// Restores the previous parser state if a rollback parser is set, reverting any temporary parser switch.
    /// </summary>
    public void SwitchBack();

    /// <summary>
    /// Transfers parsing control back to the parent controller's value parser.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if there is no parent controller to switch to.
    /// </exception>
    public void SwitchUp();
    
}
