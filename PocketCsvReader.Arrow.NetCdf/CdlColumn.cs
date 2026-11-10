using System.Buffers.Binary;
using Apache.Arrow;
using Apache.Arrow.Types;

namespace PocketCsvReader.Arrow.NetCdf;

internal readonly record struct CdlAxis(long RowStride, long Extent, long SourceStride);

internal sealed class CdlColumn(CdlVariable? variable, IArrowType type, bool encode, CdlAxis[] axes)
{
    private const int PageCells = 1024;
    private byte[]? _page;
    private long _pageStart = -PageCells;

    public async ValueTask<IArrowArray> ReadAsync(CdlStore store, long first, int length, CancellationToken token)
    {
        var values = new Values(type, encode ? 0 : length);
        if (!encode)
        {
            for (var row = 0; row < length; row++)
            {
                token.ThrowIfCancellationRequested();
                values.Append(await GetValueAsync(store, first + row, token).ConfigureAwait(false));
            }

            return values.Build();
        }

        var ends = new Int32Array.Builder();
        var run = await GetValueAsync(store, first, token).ConfigureAwait(false);
        for (var row = 1; row < length; row++)
        {
            token.ThrowIfCancellationRequested();
            var next = await GetValueAsync(store, first + row, token).ConfigureAwait(false);
            if (next == run)
                continue;
            values.Append(run);
            ends.Append(row);
            run = next;
        }

        values.Append(run);
        ends.Append(length);
        return new RunEndEncodedArray(ends.Build(), values.Build());
    }

    private ValueTask<CdlValue> GetValueAsync(CdlStore store, long row, CancellationToken token)
    {
        var index = 0L;
        foreach (var axis in axes)
            index += ((row / axis.RowStride) % axis.Extent) * axis.SourceStride;

        if (variable is null)
            return ValueTask.FromResult(new CdlValue((ulong)index));
        if (index >= variable.ValueCount)
            return ValueTask.FromResult(default(CdlValue));
        if (index >= _pageStart && index - _pageStart < PageCells)
            return ValueTask.FromResult(ReadCell(index));

        return LoadCellAsync(store, variable, index, token);
    }

    private async ValueTask<CdlValue> LoadCellAsync(CdlStore store, CdlVariable source, long index, CancellationToken token)
    {
        _page ??= new byte[PageCells * CdlStore.CellSize];
        var start = index - (index % PageCells);
        var cells = (int)Math.Min(PageCells, source.ValueCount - start);
        _pageStart = -PageCells; // A cancelled read may have partially overwritten the previous page.
        await store.ReadAsync(source.Offset + (start * CdlStore.CellSize),
            _page.AsMemory(0, cells * CdlStore.CellSize), token).ConfigureAwait(false);
        _pageStart = start;
        return ReadCell(index);
    }

    private CdlValue ReadCell(long index)
    {
        var offset = (int)(index - _pageStart) * CdlStore.CellSize;
        var bits = BinaryPrimitives.ReadUInt64LittleEndian(_page.AsSpan(offset, sizeof(ulong)));
        return new CdlValue(bits, _page![offset + sizeof(ulong)] != 0);
    }

    private sealed class Values(IArrowType type, int capacity)
    {
        private readonly int _width = ((FixedWidthType)type).BitWidth / 8;
        private readonly ArrowBuffer.Builder<byte> _values = new(checked(capacity * (((FixedWidthType)type).BitWidth / 8)));
        private readonly ArrowBuffer.BitmapBuilder _validity = new(capacity);
        private int _nulls;

        public void Append(CdlValue value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, value.Bits);
            _values.Append(bytes[.._width]);
            _validity.Append(value.IsValid);
            if (!value.IsValid)
                _nulls++;
        }

        public IArrowArray Build()
        {
            var validity = _nulls == 0 ? ArrowBuffer.Empty : _validity.Build();
            return ArrowArrayFactory.BuildArray(new ArrayData(type, _validity.Length, _nulls, 0, [validity, _values.Build()], []));
        }
    }
}
