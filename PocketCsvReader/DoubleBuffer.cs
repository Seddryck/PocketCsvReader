using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
internal class DoubleBuffer : IBufferReader
{
    private StreamBuffer _first;
    private StreamBuffer _second;

    private StreamBuffer CurrentBuffer { get; set; }
    private StreamBuffer ReadAheadBuffer { get; set; }
    protected Task? ReadAhead { get; private set; }

    public DoubleBuffer(StreamReader reader, int maxLength, ArrayPool<char>? pool = null)
    {
        _first = new StreamBuffer(reader, maxLength, pool);
        _second = new StreamBuffer(reader, maxLength, pool);
        CurrentBuffer = _first;
        ReadAheadBuffer = _second;
    }

    public ReadOnlyMemory<char> Read()
    {
        if (CurrentBuffer.IsEof)
            throw new InvalidOperationException();

        if (ReadAhead is null)
            CurrentBuffer.Read();
        else
        {
            ReadAhead.Wait();
            SwitchBuffers();
        }
        ReadAhead = ReadAheadBuffer.ReadAsync();
        return CurrentBuffer.Memory;
    }

    private void SwitchBuffers()
    {
        ReadAheadBuffer = ReadAheadBuffer == _first ? _second : _first;
        CurrentBuffer = CurrentBuffer == _first ? _second : _first;
    }

    public bool IsEof => CurrentBuffer.IsEof;

    public void Dispose()
    {
        _first.Dispose();
        _second.Dispose();
    }
}
