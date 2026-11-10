using System.Buffers.Binary;

namespace PocketCsvReader.Arrow.NetCdf;

internal readonly record struct CdlValue(ulong Bits, bool IsValid = true);

internal sealed class CdlStore(bool asynchronous) : IDisposable
{
    internal const int CellSize = sizeof(ulong) + 1;
    private const int WriteCells = 4096;
    private FileStream? _file;
    private byte[]? _pending;
    private int _length;
    private long _written;

    public long Position => _written + _length;

    public ValueTask AppendAsync(CdlValue value, CancellationToken token)
    {
        _pending ??= new byte[WriteCells * CellSize];
        BinaryPrimitives.WriteUInt64LittleEndian(_pending.AsSpan(_length, sizeof(ulong)), value.Bits);
        _pending[_length + sizeof(ulong)] = value.IsValid ? (byte)1 : (byte)0;
        _length += CellSize;
        return _length == _pending.Length ? FlushAsync(token) : ValueTask.CompletedTask;
    }

    public async ValueTask CompleteAsync(CancellationToken token)
    {
        await FlushAsync(token).ConfigureAwait(false);
        _pending = null;
    }

    private async ValueTask FlushAsync(CancellationToken token)
    {
        if (_length == 0)
            return;

        _file ??= OpenTemporaryFile();
        if (asynchronous)
            await _file.WriteAsync(_pending.AsMemory(0, _length), token).ConfigureAwait(false);
        else
            _file.Write(_pending.AsSpan(0, _length));
        _written += _length;
        _length = 0;
    }

    public ValueTask ReadAsync(long offset, Memory<byte> target, CancellationToken token)
    {
        var file = _file ?? throw new InvalidOperationException("No CDL data was staged.");
        file.Position = offset;
        if (asynchronous)
            return file.ReadExactlyAsync(target, token);

        file.ReadExactly(target.Span);
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _file?.Dispose();

    private FileStream OpenTemporaryFile()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 1,
            Options = FileOptions.DeleteOnClose | (asynchronous ? FileOptions.Asynchronous : FileOptions.None),
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(Path.Combine(Path.GetTempPath(), $"pocketcsvreader-cdl-{Path.GetRandomFileName()}"), options);
    }
}
