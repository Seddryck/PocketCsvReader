using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;

public interface IBufferReader : IDisposable
{
    ReadOnlyMemory<char> Read();
    bool IsEof { get; }
    void Reset();
}
