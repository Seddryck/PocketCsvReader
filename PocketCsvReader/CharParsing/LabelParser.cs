using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
readonly struct LabelParser
{
    private readonly FieldParser _ctx;

    public LabelParser(FieldParser ctx) => _ctx = ctx;

    public void Parse(char c, int pos)
    {
        throw new NotImplementedException();
    }
}
