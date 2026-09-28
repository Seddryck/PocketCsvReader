using PocketCsvReader;
using PocketCsvReader.Configuration;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.FixedWidth;

public sealed class FixedWidthProfile : IProfile
{
    public FixedWidthDescriptor Descriptor { get; }
    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }
    public ParserOptimizationOptions ParserOptimizations { get; }
    public StreamInitializationOptions StreamInitialization => StreamInitializationOptions.ForwardOnly;
    public int RecordWidth { get; }

    public FixedWidthProfile(
        FixedWidthDescriptor descriptor,
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Fields is null || descriptor.Fields.Count == 0)
            throw new ArgumentException("At least one fixed-width field must be defined.", nameof(descriptor));
        if (string.IsNullOrEmpty(descriptor.LineTerminator))
            throw new ArgumentException("The line terminator cannot be null or empty.", nameof(descriptor));

        var ordered = descriptor.Fields.OrderBy(field => field.Offset).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var field = ordered[i];
            if (string.IsNullOrWhiteSpace(field.Name))
                throw new ArgumentException("Every fixed-width field must have a name.", nameof(descriptor));
            if (field.Offset < 0)
                throw new ArgumentException($"Field '{field.Name}' has a negative offset.", nameof(descriptor));
            if (field.Width <= 0)
                throw new ArgumentException($"Field '{field.Name}' must have a positive width.", nameof(descriptor));
            if (i > 0 && ordered[i - 1].Offset + ordered[i - 1].Width > field.Offset)
                throw new ArgumentException($"Field '{field.Name}' overlaps field '{ordered[i - 1].Name}'.", nameof(descriptor));
        }

        var duplicateName = descriptor.Fields
            .GroupBy(field => field.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
            throw new ArgumentException($"Field name '{duplicateName.Key}' is defined more than once.", nameof(descriptor));

        Descriptor = descriptor with { Fields = descriptor.Fields.ToArray() };
        Schema = schema;
        Resource = resource;
        Parsers = parsers;
        var requestedOptimizations = parserOptimizations ?? new ParserOptimizationOptions();
        if (requestedOptimizations.BufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(parserOptimizations), "The parser buffer size must be positive.");
        ParserOptimizations = requestedOptimizations with { RowCountAtStart = false };
        RecordWidth = descriptor.Fields.Max(field => checked(field.Offset + field.Width));
    }
}
