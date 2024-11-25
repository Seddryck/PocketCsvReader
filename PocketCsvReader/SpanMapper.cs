using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public delegate T SpanMapper<T>(ReadOnlySpan<char> span, IEnumerable<FieldSpan> fieldSpans);
