using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public static class StreamExtensions
{
    public static void Rewind(this StreamReader reader)
    {
        reader.BaseStream.Position = 0;
        reader.DiscardBufferedData();
    }
}
