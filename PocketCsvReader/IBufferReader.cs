using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;

public interface IBufferReader : IDisposable
{
    ReadOnlyMemory<char> Read();
    ValueTask<ReadOnlyMemory<char>> ReadAsync(CancellationToken cancellationToken = default);
    bool IsEof { get; }
    void Reset();
}
