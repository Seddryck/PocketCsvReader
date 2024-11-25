using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public record struct FieldSpan
(
    int Start,
    int Length,
    bool WasQuoted,
    bool IsEscaped
);
