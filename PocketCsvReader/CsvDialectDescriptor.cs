using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader
{
    public class CsvDialectDescriptor
    {
        public char Delimiter { get; internal set; } = ',';
        public string LineTerminator { get; internal set; } = "\r\n";
        public char QuoteChar { get; internal set; } = '"';
        public bool DoubleQuote { get; internal set; } = false; //should be true?
        public char EscapeChar { get; internal set; }
        public string? NullSequence { get; internal set; } = null;
        public bool SkipInitialSpace { get; internal set; } = false;
        public bool Header { get; internal set; } = true;
        public char CommentChar { get; internal set; }
        public bool CaseSensitiveHeader { get; internal set; } = false;
        public string CsvDdfVersion { get; internal set; } = "1.0";
    }
}
