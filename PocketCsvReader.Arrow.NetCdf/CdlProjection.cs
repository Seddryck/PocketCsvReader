using System.Globalization;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;

namespace PocketCsvReader.Arrow.NetCdf;

internal sealed class CdlProjection(
    Schema schema, CdlStore store, CdlColumn[] columns, long rowCount, int batchSize) : IArrowArrayStream
{
    private long _nextRow;
    private bool _disposed;

    public Schema Schema => schema;

    public static CdlProjection Create(CdlDataset dataset, CdlStore store, NetCdfCdlOptions options)
    {
        var variables = SelectVariables(dataset, options.DataVariables);
        var dimensions = options.RowDimensions is { } axes
            ? axes.Select(name => dataset.Dimensions.TryGetValue(name, out var axis)
                ? axis : throw new InvalidDataException($"Unknown CDL row dimension '{name}'.")).ToArray()
            : InferDimensions(dataset, variables);

        var extent = CdlDataset.Size(dimensions, "the selected row dimensions");
        var count = dataset.HasValues ? extent : 0;
        var columns = CreateColumns(dataset, dimensions, variables, count > 0, options.RunEndEncodeCoordinates);
        var metadata = new Dictionary<string, string>(dataset.Attributes, StringComparer.Ordinal)
        {
            ["netcdf:name"] = dataset.Name,
            ["netcdf:dimensions"] = Names(dimensions),
            ["netcdf:shape"] = Shape(dimensions),
        };
        var schema = new Schema(columns.Select(entry => entry.Field), metadata);
        return new CdlProjection(schema, store, columns.Select(entry => entry.Column).ToArray(), count, options.BatchSize);
    }

    private static CdlVariable[] SelectVariables(CdlDataset dataset, IReadOnlyList<string>? names)
    {
        if (names is null)
            return dataset.Variables.Values.Where(variable => !IsCoordinate(variable)).ToArray();
        return names.Select(name => dataset.Variables.TryGetValue(name, out var variable)
            ? variable : throw new InvalidDataException($"Unknown CDL data variable '{name}'.")).ToArray();
    }

    private static List<(Field Field, CdlColumn Column)> CreateColumns(
        CdlDataset dataset, CdlDimension[] dimensions, CdlVariable[] variables, bool hasRows, bool encodeCoordinates)
    {
        var columns = new List<(Field, CdlColumn)>();
        var rowStrides = Strides(dimensions, hasRows);

        foreach (var dimension in dimensions)
        {
            dataset.Variables.TryGetValue(dimension.Name, out var variable);
            var coordinate = variable is not null && IsCoordinate(variable) ? variable : null;
            var sourceDimensions = coordinate?.Dimensions ?? [dimension];
            var mapping = MapAxes(dimension.Name, sourceDimensions, dimensions, rowStrides, hasRows);
            columns.Add(CreateColumn(dimension.Name, coordinate, sourceDimensions, encodeCoordinates, mapping));
        }

        foreach (var variable in variables)
        {
            if (IsCoordinate(variable) && dimensions.Contains(variable.Dimensions[0]))
                continue;
            if (dimensions.Any(dimension => dimension.Name == variable.Name))
                throw new InvalidDataException($"Variable '{variable.Name}' conflicts with the index column for row dimension '{variable.Name}'. Select other data variables or row dimensions.");
            var mapping = MapAxes(variable.Name, variable.Dimensions, dimensions, rowStrides, hasRows);
            columns.Add(CreateColumn(variable.Name, variable, variable.Dimensions, false, mapping));
        }

        return columns;
    }

    private static CdlAxis[] MapAxes(
        string name, CdlDimension[] sourceDimensions, CdlDimension[] dimensions, long[] rowStrides, bool hasRows)
    {
        var sourceStrides = Strides(sourceDimensions, hasRows && CdlDataset.Size(sourceDimensions, $"variable '{name}'") > 0);
        var mapping = new CdlAxis[sourceDimensions.Length];
        for (var i = 0; i < sourceDimensions.Length; i++)
        {
            var dimension = sourceDimensions[i];
            var axis = System.Array.IndexOf(dimensions, dimension);
            if (axis < 0)
                throw new InvalidDataException($"Variable '{name}' has dimensions [{Names(sourceDimensions)}] outside the row dimensions [{Names(dimensions)}]. Select compatible data variables or specify row dimensions that include '{dimension.Name}'.");
            mapping[i] = new CdlAxis(rowStrides[axis], dimension.Extent, sourceStrides[i]);
        }

        return mapping;
    }

    private static (Field Field, CdlColumn Column) CreateColumn(
        string name, CdlVariable? variable, CdlDimension[] sourceDimensions, bool encode, CdlAxis[] mapping)
    {
        var type = variable?.Type ?? Int64Type.Default;
        var fieldType = encode ? new RunEndEncodedType(Int32Type.Default, type) : type;
        var metadata = variable is null
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["netcdf:coordinate"] = "zero-based index" }
            : new Dictionary<string, string>(variable.Attributes, StringComparer.Ordinal);
        metadata.Add("netcdf:dimensions", Names(sourceDimensions));
        metadata.Add("netcdf:shape", Shape(sourceDimensions));
        return (new Field(name, fieldType, variable is not null, metadata), new CdlColumn(variable, type, encode, mapping));
    }

    public async ValueTask<RecordBatch?> ReadNextRecordBatchAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_nextRow == rowCount)
            return null;

        var length = (int)Math.Min(batchSize, rowCount - _nextRow);
        var arrays = new IArrowArray[columns.Length];
        try
        {
            for (var i = 0; i < columns.Length; i++)
                arrays[i] = await columns[i].ReadAsync(store, _nextRow, length, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var batch = new RecordBatch(Schema, arrays, length);
            _nextRow += length;
            return batch;
        }
        catch
        {
            foreach (var array in arrays)
                array?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        store.Dispose();
    }

    private static CdlDimension[] InferDimensions(CdlDataset dataset, CdlVariable[] variables)
    {
        var candidates = variables.Length == 0 ? dataset.Variables.Values.Where(IsCoordinate).ToArray() : variables;
        if (candidates.Length == 0 && dataset.Dimensions.Count <= 1)
            return dataset.Dimensions.Values.ToArray();

        var widest = candidates.MaxBy(variable => variable.Dimensions.Length)?.Dimensions;
        if (widest is not null && candidates.All(variable => variable.Dimensions.Length == widest.Length
            ? variable.Dimensions.SequenceEqual(widest)
            : variable.Dimensions.All(widest.Contains)))
            return widest;

        var shapes = candidates.Length == 0
            ? Names(dataset.Dimensions.Values)
            : string.Join("; ", candidates.Select(variable => $"{variable.Name}({Names(variable.Dimensions)})"));
        throw new InvalidDataException($"Cannot infer a single ordered CDL row shape from [{shapes}]. Specify RowDimensions and select compatible DataVariables.");
    }

    private static bool IsCoordinate(CdlVariable variable)
        => variable.Dimensions is [var dimension] && dimension.Name == variable.Name;

    private static string Names(IEnumerable<CdlDimension> dimensions) => string.Join(",", dimensions.Select(dimension => dimension.Name));

    private static string Shape(IEnumerable<CdlDimension> dimensions)
        => string.Join(",", dimensions.Select(dimension => dimension.Extent.ToString(CultureInfo.InvariantCulture)));

    private static long[] Strides(CdlDimension[] dimensions, bool hasRows)
    {
        var strides = new long[dimensions.Length];
        if (hasRows)
        {
            var stride = 1L;
            for (var i = dimensions.Length - 1; i >= 0; i--)
            {
                strides[i] = stride;
                stride *= dimensions[i].Extent;
            }
        }

        return strides;
    }
}
