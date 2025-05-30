﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public class SingleBuffer : IBufferReader
{
    private StreamBuffer _streamBuffer;

    public SingleBuffer(StreamReader reader, int maxLength, ArrayPool<char>? pool = null)
        => _streamBuffer = new StreamBuffer(reader, maxLength, pool);

    public ReadOnlyMemory<char> Read()
    {
        _streamBuffer.Read();
        return _streamBuffer.Memory;
    }

    public bool IsEof => _streamBuffer.IsEof;

    public void Reset()
        => _streamBuffer.Reset();

    public void Dispose()
    {
        _streamBuffer.Dispose();
    }
}
