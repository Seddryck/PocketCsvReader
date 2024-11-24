using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public interface IInternalCharParser
{
    ParserState Parse(char c);
}
