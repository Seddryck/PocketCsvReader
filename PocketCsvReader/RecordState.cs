using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public enum RecordState
{
    Record = 0,
    Comment = 1,
    Header = 2,
    Eof = 3,
}
