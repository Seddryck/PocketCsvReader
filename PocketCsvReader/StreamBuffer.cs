using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
internal class StreamBuffer : IDisposable
{
    private StreamReader Reader { get; }
    private int MaxLength { get; }
    private ArrayPool<char>? Pool { get; }
    private char[] _arrayChar;
    private int? _length;
    public int Length => _length ?? MaxLength;
    public ReadOnlyMemory<char> Memory { get; private set; }
    public bool IsEmpty => !_length.HasValue;
    public bool IsEof { get; private set; }

    public StreamBuffer(StreamReader reader, int maxLength, ArrayPool<char>? pool = null)
    {
        Reader = reader;
        MaxLength = maxLength;
        _arrayChar = pool?.Rent(maxLength) ?? new char[maxLength];
        Memory = _arrayChar.AsMemory(0, Length);
        IsEof = false;
        Pool = pool;
    }

    public async Task ReadAsync()
    {
        _length = null;
        _length = await Reader.ReadAsync(_arrayChar, 0, MaxLength);
        IsEof = _length < MaxLength;
        Memory = _arrayChar.AsMemory(0, _length.Value);
    }

    public void Read()
    {
        _length = Reader.Read(_arrayChar, 0, MaxLength);
        IsEof = _length < MaxLength;
        Memory = _arrayChar.AsMemory(0, _length.Value);
    }

    public void Dispose()
    {
        Pool?.Return(_arrayChar);
    }
}
