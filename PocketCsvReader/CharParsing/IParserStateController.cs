using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
public interface IParserStateController
{
    /// <summary>
    /// Processes a single character at the specified position and returns the resulting parser state.
    /// </summary>
    /// <param name="c">The character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>The updated parser state after processing the character.</returns>
    ParserState Parse(char c, int pos);
    /// <summary>
    /// Handles the end-of-file condition during parsing at the specified position.
    /// </summary>
    /// <param name="pos">The position in the input where end-of-file is encountered.</param>
    /// <returns>The resulting parser state after processing end-of-file.</returns>
    ParserState ParseEof(int pos);
    /// <summary>
    /// Switches the parser to the value parsing state.
    /// </summary>
    void SwitchToValue();
    /// <summary>
    /// Switches the parser state to handle quoted values.
    /// </summary>
    void SwitchToQuoted();
    /// <summary>
    /// Switches the parser state to interpret input as raw, unprocessed text.
    /// </summary>
    void SwitchToRaw();
    /// <summary>
    /// Switches the parser state to handle array values.
    /// </summary>
    void SwitchToArray();
    /// <summary>
    /// Switches the parser state to handle comment sections.
    /// </summary>
    void SwitchToComment();
    /// <summary>
    /// Switches the parser to the line terminator state using the specified parser state.
    /// </summary>
    /// <param name="state">The parser state to use for the line terminator transition.</param>
    void SwitchToLineTerminator(ParserState state);
    /// <summary>
    /// Reverts the parser to the previous state in the state stack.
    /// </summary>
    void SwitchBack();
    /// <summary>
    /// Moves the parser state up one level in the state hierarchy.
    /// </summary>
    void SwitchUp();
    /// <summary>
    /// Resets the parser state controller to its initial state.
    /// </summary>
    void Reset();
}
