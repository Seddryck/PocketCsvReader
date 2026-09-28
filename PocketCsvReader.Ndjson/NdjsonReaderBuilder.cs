using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Ndjson.Configuration;
public class NdjsonReaderBuilder : ReaderBuilder<NdjsonReaderBuilder>
{
    private NdjsonDialectDescriptorBuilder _dialectBuilder = new();

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

    public NdjsonReader Build()
        => new(new NdjsonProfile(
            _dialectBuilder.Build(),
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations()));
}
