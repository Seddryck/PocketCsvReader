using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

internal class FirstCharOfArrayFieldParser : IInternalCharParser
{
    private CharParser Parser { get; }
    private CharOfArrayFieldParser ArrayParser { get; }
    public FirstCharOfArrayFieldParser(CharParser parser, CharOfArrayFieldParser arrayParser)
        => (Parser, ArrayParser)
                = (parser ?? throw new ArgumentNullException(nameof(parser)),
                    arrayParser ?? throw new ArgumentNullException(nameof(arrayParser)));

    public virtual ParserState Parse(char c)
    {
        Parser.SetFieldStart();
        Parser.Switch(Parser.CharOfArrayField);
        return ArrayParser.Parse(c);
    }
}
