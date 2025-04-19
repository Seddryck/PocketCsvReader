using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;

public enum CompressionFormat
{
    None = 0,
    Gzip = 1,
    Deflate = 2,
    Zip = 3
}
