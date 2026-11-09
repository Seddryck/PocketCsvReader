using Apache.Arrow;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

// One backing file per returned record. Offsets point to complete UTF-8 JSON values.
internal sealed class RawColumnStore : IJsonColumnSource
{
    internal delegate IArrowArray DecodeColumn(BufferedJsonInput input, Field field, int length, JsonFieldBuffer scratch);

    private readonly FileStream _file;
    private readonly DecodeColumn _decode;
    private readonly long[] _offsets;
    private readonly bool _asynchronous;
    private StreamWriter? _writer;

    public RawColumnStore(bool asynchronous, int columnCount, DecodeColumn decode)
    {
        _asynchronous = asynchronous;
        _decode = decode;
        _offsets = new long[columnCount];
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.DeleteOnClose | (asynchronous ? FileOptions.Asynchronous : FileOptions.None),
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        _file = new FileStream(path, options);
        try
        {
            _writer = new StreamWriter(_file, leaveOpen: true);
        }
        catch
        {
            _file.Dispose();
            throw;
        }
    }

    public void MarkColumn(int index) => _offsets[index] = _file.Position;

    public ValueTask WriteAsync(ReadOnlyMemory<char> text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var writer = Writer();
        if (_asynchronous)
            return new ValueTask(writer.WriteAsync(text, cancellationToken));
        writer.Write(text.Span);
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var writer = Writer();
        if (_asynchronous)
            return new ValueTask(writer.FlushAsync(cancellationToken));
        writer.Flush();
        return ValueTask.CompletedTask;
    }

    public void CompleteWriting()
    {
        Writer().Dispose();
        _writer = null;
    }

    public IArrowArray ReadColumn(int index, Field field, int length, JsonFieldBuffer scratch)
    {
        _file.Position = _offsets[index];
        using var input = new BufferedJsonInput(_file, leaveOpen: true, asynchronous: false);
        return _decode(input, field, length, scratch);
    }

    public void Dispose()
    {
        try
        {
            _writer?.Dispose();
        }
        finally
        {
            _file.Dispose();
        }
    }

    private StreamWriter Writer()
        => _writer ?? throw new InvalidOperationException("The raw columns have already been written.");
}
