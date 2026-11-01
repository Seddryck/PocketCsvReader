using PocketCsvReader.Configuration;

namespace PocketCsvReader.SecurityLogs;

public enum SecurityLogFormat { Cef, Leef }

public sealed class SecurityLogProfile : IProfile
{
    public SecurityLogFormat Format { get; }
    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }
    public ParserOptimizationOptions ParserOptimizations { get; }
    public bool TrackPosition => ParserOptimizations.TrackPosition;
    public StreamInitializationOptions StreamInitialization => StreamInitializationOptions.ForwardOnly;
    /// <summary>Selected attribute keys, or null to discover them from the first record.</summary>
    public IReadOnlyList<string>? Attributes { get; }

    public SecurityLogProfile(SecurityLogFormat format, IEnumerable<string>? attributes = null,
        SchemaDescriptor? schema = null, ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null, ParserOptimizationOptions? parserOptimizations = null)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        var options = parserOptimizations ?? new();
        if (options.BufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(parserOptimizations));
        var selected = attributes?.ToArray();
        if (selected is not null)
        {
            var names = new HashSet<string>(SecurityLogParser.HeaderNames(format), StringComparer.Ordinal);
            foreach (var name in selected)
                if (string.IsNullOrWhiteSpace(name) || name.Any(c => char.IsWhiteSpace(c) || c is '=' or '|' or '\\') || !names.Add(name))
                    throw new ArgumentException("Attribute names must be nonempty, unique, and distinct from header names.", nameof(attributes));
        }
        Format = format;
        Attributes = selected is null ? null : Array.AsReadOnly(selected);
        Schema = schema;
        Resource = resource;
        Parsers = parsers;
        ParserOptimizations = options with { RowCountAtStart = false };
    }
}
