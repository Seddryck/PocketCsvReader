using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
readonly struct LabelParser
{
    private readonly FieldParser _ctx;

    /// <summary>
/// Initializes a new instance of the <see cref="LabelParser"/> struct with the specified field parser context.
/// </summary>
public LabelParser(FieldParser ctx) => _ctx = ctx;

    /// <summary>
    /// Placeholder for label parsing logic; not yet implemented.
    /// </summary>
    /// <param name="c">The character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <exception cref="NotImplementedException">Always thrown as this method is not implemented.</exception>
    public void Parse(char c, int pos)
    {
        throw new NotImplementedException();
    }
}
