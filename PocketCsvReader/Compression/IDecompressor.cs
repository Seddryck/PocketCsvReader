using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Compression;
public interface IDecompressor
{
    Stream Decompress(Stream stream);
}
