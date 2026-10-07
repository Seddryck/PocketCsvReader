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
    {
        ArgumentNullException.ThrowIfNull(configure);
        _projectedProperties = configure(new JsonProjectionBuilder()).Build();
        return this;
    }

    public NdjsonReader Build()
        => new(new NdjsonProfile(
            _dialectBuilder.Build(),
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations(),
            _projectedProperties));
}
