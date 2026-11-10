using Apache.Arrow.Types;

namespace PocketCsvReader.Arrow.NetCdf;

internal sealed class CdlDimension(string name, long extent, bool unlimited)
{
    public string Name { get; } = name;
    public long Extent { get; set; } = extent;
    public bool Unlimited { get; } = unlimited;
}

internal sealed class CdlVariable(string name, IArrowType type, CdlDimension[] dimensions)
{
    public string Name { get; } = name;
    public IArrowType Type { get; } = type;
    public CdlDimension[] Dimensions { get; } = dimensions;
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);
    public List<ulong> MissingValues { get; } = [];
    public long Offset { get; set; }
    public long ValueCount { get; set; }
    public bool Assigned { get; set; }
}

internal sealed class CdlDataset
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, CdlDimension> Dimensions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, CdlVariable> Variables { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);
    public CdlDimension? Unlimited { get; set; }
    public bool HasValues { get; set; }

    public static long Size(ReadOnlySpan<CdlDimension> dimensions, string description)
    {
        foreach (var dimension in dimensions)
        {
            if (dimension.Extent == 0)
                return 0;
        }

        var size = 1L;
        foreach (var dimension in dimensions)
        {
            if (size > long.MaxValue / dimension.Extent)
                throw new InvalidDataException($"The extent product of {description} exceeds Int64.");
            size *= dimension.Extent;
        }

        return size;
    }
}
