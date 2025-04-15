using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
public interface IParserContext
{
    /// <summary>
/// Marks the beginning of a label at the specified position, indicating whether it is quoted.
/// </summary>
/// <param name="pos">The character position where the label starts.</param>
/// <param name="quoted">True if the label is enclosed in quotes; otherwise, false.</param>
void StartLabel(int pos, bool quoted);
    /// <summary>
/// Marks the end position of a label during parsing.
/// </summary>
/// <param name="pos">The character index where the label ends.</param>
void EndLabel(int pos);

    /// <summary>
/// Marks the beginning of a value at the specified position, indicating whether the value is quoted.
/// </summary>
/// <param name="pos">The character position where the value starts.</param>
/// <param name="quoted">True if the value is enclosed in quotes; otherwise, false.</param>
void StartValue(int pos, bool quoted);
    /// <summary>
/// Marks the end position of a value in the parsed data.
/// </summary>
/// <param name="pos">The position in the input where the value ends.</param>
void EndValue(int pos);
    /// <summary>
/// Handles the occurrence of an empty value in the parsing context.
/// </summary>
void EmptyValue();
    IParserContext? Parent { get; }
    /// <summary>
/// Adds a child field span to the current parsing context.
/// </summary>
/// <param name="span">The <see cref="FieldSpan"/> representing the child span to add.</param>
void AddChild(FieldSpan span);

    /// <summary>
/// Marks the beginning of an escaping sequence in the parsing context.
/// </summary>
void StartEscaping();
    /// <summary>
/// Marks the end of an escaping sequence within the parsing context.
/// </summary>
void EndEscaping();
    /// <summary>
/// Removes the current escaping state from the parsing context.
/// </summary>
void RemoveEscaping();
    /// <summary>
/// Marks the beginning of a doubling sequence, typically used to handle doubled quote characters in CSV parsing.
/// </summary>
void StartDoubling();
    /// <summary>
/// Marks the end of a doubling sequence within the parsing context.
/// </summary>
void EndDoubling();
    /// <summary>
/// Clears the current doubling state in the parsing context.
/// </summary>
void RemoveDoubling();
    bool Escaping { get; }
    bool Doubling { get; }

    ref FieldSpan Span { get; }
    bool IsComplete { get; }
    /// <summary>
/// Resets the parsing context to its initial state, clearing any accumulated parsing data or state flags.
/// </summary>
void Reset();
}
