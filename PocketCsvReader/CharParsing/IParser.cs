using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
public interface IParser
{
    /// <summary>
/// Processes a single character at the specified position and returns the resulting parser state.
/// </summary>
/// <param name="c">The character to parse.</param>
/// <param name="pos">The position of the character in the input.</param>
/// <returns>The parser state after processing the character.</returns>
ParserState Parse(char c, int pos);
    /// <summary>
/// Processes the end-of-file condition at the specified position and returns the resulting parser state.
/// </summary>
/// <param name="pos">The position in the input where end-of-file is encountered.</param>
/// <returns>The parser state after handling end-of-file.</returns>
ParserState ParseEof(int pos);
    /// <summary>
/// Resets the parser to its initial state, clearing any accumulated parsing data.
/// </summary>
void Reset();
    ref FieldSpan Result { get; }
}
