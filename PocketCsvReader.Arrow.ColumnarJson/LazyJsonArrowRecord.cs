using System.Runtime.ExceptionServices;
using Apache.Arrow;
using Apache.Arrow.Types;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

internal sealed class LazyJsonArrowRecord(
    Schema schema, int length, IJsonColumnSource source) : IArrowRecord
{
    private readonly object _gate = new();
    private readonly IArrowArray?[] _columns = new IArrowArray?[schema.FieldsList.Count];
    private readonly ExceptionDispatchInfo?[] _errors = new ExceptionDispatchInfo?[schema.FieldsList.Count];
    private readonly JsonFieldBuffer _scratch = new();
    private bool _disposed;

    public IRecordType Schema { get; } = schema;
    public int Length { get; } = length;
    public int ColumnCount => _columns.Length;
    public int Offset => 0;
    public int NullCount => 0;
    public ArrayData Data => throw new NotSupportedException("Unable to get data for a record.");

    public static LazyJsonArrowRecord FromFile(
        Schema schema, int length, SharedJsonFile file, SharedJsonFile.ReadColumn read)
    {
        var columns = file.RetainColumns(read);
        try
        {
            return new LazyJsonArrowRecord(schema, length, columns);
        }
        catch
        {
            columns.Dispose();
            throw;
        }
    }

    public IArrowArray Column(string columnName, IEqualityComparer<string>? comparer = null)
        => Column(schema.GetFieldIndex(columnName, comparer));

    public IArrowArray Column(int columnIndex)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(columnIndex, ColumnCount);
            if (_columns[columnIndex] is { } cached)
                return cached;
            _errors[columnIndex]?.Throw();

            try
            {
                var array = source.ReadColumn(columnIndex, schema.FieldsList[columnIndex], Length, _scratch);
                _columns[columnIndex] = array;
                return array;
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
                _errors[columnIndex] = ExceptionDispatchInfo.Capture(exception);
                throw;
            }
        }
    }

    public bool IsNull(int index) => false;
    public bool IsValid(int index) => true;

    public void Accept(IArrowArrayVisitor visitor)
    {
        if (visitor is IArrowArrayVisitor<IArrowRecord> recordVisitor)
            recordVisitor.Visit(this);
        else
            visitor.Visit(this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                foreach (var column in _columns)
                    column?.Dispose();
            }
            finally
            {
                source.Dispose();
            }
        }
    }
}
