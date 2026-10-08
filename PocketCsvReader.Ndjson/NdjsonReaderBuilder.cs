using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Ndjson.Configuration;
public class NdjsonReaderBuilder : ReaderBuilder<NdjsonReaderBuilder>
{
    private NdjsonDialectDescriptorBuilder _dialectBuilder = new();
    private string[]? _projectedProperties;
    private bool _orderedProjection;
    private bool _stableObjectShape;

    public NdjsonReaderBuilder()
        : base(NdjsonProfile.DefaultParserOptimizations)
    { }

    public NdjsonReaderBuilder WithDialect(Func<NdjsonDialectDescriptorBuilder, NdjsonDialectDescriptorBuilder> func)
    {
        _dialectBuilder = func(_dialectBuilder);
        return this;
    }
    public NdjsonReaderBuilder WithDialect(NdjsonDialectDescriptorBuilder dialectBuilder)
    {
        _dialectBuilder = dialectBuilder;
        return this;
    }

    /// <summary>
    /// Selects the JSON object properties exposed by each NDJSON record.
    /// Properties may occur in any order in the input and are exposed in projection order.
    /// </summary>
    public NdjsonReaderBuilder WithProjection(Func<JsonProjectionBuilder, JsonProjectionBuilder> configure)
        => ConfigureProjection(configure, ordered: false);

    /// <summary>
    /// Projects properties in the order in which they must occur in each NDJSON object.
    /// Non-projected properties may occur between projected properties.
    /// </summary>
    public NdjsonReaderBuilder WithOrderedProjection(Func<JsonProjectionBuilder, JsonProjectionBuilder> configure)
        => ConfigureProjection(configure, ordered: true);

    /// <summary>
    /// Trusts that every object has the same top-level properties in the same order.
    /// The first object establishes labels and ordinals. Later property names are validated as JSON syntax
    /// but are not decoded or compared; renames or reordering with the same count can therefore map values
    /// to the wrong ordinal. Objects with a different property count are rejected.
    /// </summary>
    public NdjsonReaderBuilder WithStableObjectShape()
    {
        _stableObjectShape = true;
        return this;
    }

    private NdjsonReaderBuilder ConfigureProjection(
        Func<JsonProjectionBuilder, JsonProjectionBuilder> configure,
        bool ordered)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _projectedProperties = configure(new JsonProjectionBuilder()).Build();
        _orderedProjection = ordered;
        return this;
    }

    public NdjsonReader Build()
        => new(new NdjsonProfile(
            _dialectBuilder.Build(),
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations(),
            _projectedProperties,
            _orderedProjection,
            _stableObjectShape));
}
