using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public record struct FieldSpan
(
    int ValueStart,
    int ValueLength,
    bool WasQuoted = false,
    bool IsEscaped = false,
    int LabelStart = 0,
    int LabelLength = 0
);
