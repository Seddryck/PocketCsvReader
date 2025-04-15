using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader;
public class CharParser : ICharParser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CharParser"/> class with the specified CSV profile.
    /// </summary>
    /// <param name="profile">The CSV profile configuration to use for parsing.</param>
    public CharParser(CsvProfile profile)
    {
            
    }

    public int ValueStart => throw new NotImplementedException();

    public int ValueLength => throw new NotImplementedException();

    public int LabelStart => throw new NotImplementedException();

    public int LabelLength => throw new NotImplementedException();

    public bool IsQuotedField => throw new NotImplementedException();

    public bool IsEscapedField => throw new NotImplementedException();

    public FieldSpan[] Children => throw new NotImplementedException();

    public IInternalCharParser? InternalCharParser => throw new NotImplementedException();

    /// <summary>
/// Processes a single character and returns the current parser state.
/// </summary>
/// <param name="c">The character to parse.</param>
/// <returns>The resulting <see cref="ParserState"/> after processing the character.</returns>
/// <exception cref="NotImplementedException">Always thrown as this method is not implemented.</exception>
public ParserState Parse(char c) => throw new NotImplementedException();
    /// <summary>
/// Handles end-of-file parsing logic and returns the resulting parser state.
/// </summary>
/// <returns>The parser state after processing end-of-file.</returns>
public ParserState ParseEof() => throw new NotImplementedException();
    /// <summary>
/// Resets the parser state to its initial configuration.
/// </summary>
public void Reset() => throw new NotImplementedException();
}

public enum ParserState
{
    Continue,
    Error,
    Field,
    Record,
    Header,
    Comment,
    Eof,
}
