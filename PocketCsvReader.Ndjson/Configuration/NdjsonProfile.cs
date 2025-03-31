using System;
using System.Collections.Immutable;
using System.Reflection;

namespace PocketCsvReader.Ndjson.Configuration;

public class NdjsonProfile
{
    public DialectDescriptor Dialect { get; private set; }

    private NdjsonProfile(DialectDescriptor dialect)
        => Dialect = dialect;

    public NdjsonProfile(string recordSeparator)
    {
        Dialect = new DialectDescriptorBuilder()
            .WithLineTerminator(recordSeparator)
            .Build();
    }

    private static NdjsonProfile? _default;
    public static NdjsonProfile Default
    {
        get => _default ??= new NdjsonProfile(new DialectDescriptor());
    }

}
