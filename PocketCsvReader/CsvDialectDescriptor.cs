using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader
{
    public class CsvDialectDescriptor
    {
        public char Delimiter { get; set; } = ',';
        public string LineTerminator { get; set; } = "\r\n";
        public char QuoteChar { get; set; } = '"';
        public bool DoubleQuote { get; set; } = true;
        public char EscapeChar { get; set; }
        public string NullSequence { get; set; } = string.Empty;
        public bool SkipInitialSpace { get; set; } = false;
        public bool Header { get; set; } = true;
        public char CommentChar { get; set; }
        public bool CaseSensitiveHeader { get; set; } = false;
        public string CsvDdfVersion { get; set; } = "1.0";
    }
}
