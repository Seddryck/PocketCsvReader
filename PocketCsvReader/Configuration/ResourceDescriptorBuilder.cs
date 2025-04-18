using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class ResourceDescriptorBuilder
{
    private ResourceDescriptor Descriptor { get; set; } = new();

    public ResourceDescriptorBuilder WithEncoding(string? mime)
        => (Descriptor = Descriptor with { Encoding = mime }, Builder: this).Builder;
    public ResourceDescriptorBuilder WithoutEncoding()
        => WithEncoding(null);
    public ResourceDescriptorBuilder WithCompression(CompressionFormat compression)
    {
        if (compression == CompressionFormat.None)
            return WithoutCompression();
        else
            return WithCompression(compression.ToString().ToLowerInvariant());
    }
    public ResourceDescriptorBuilder WithCompression(string? format)
        => (Descriptor = Descriptor with { Compression = format }, Builder: this).Builder;
    public ResourceDescriptorBuilder WithoutCompression()
        => WithCompression(null);
    public ResourceDescriptorBuilder WithSequence(string pattern, string? value)
    {
        Descriptor = Descriptor with
        {
            Sequences = SequenceCollection.Concat(Descriptor.Sequences, ImmutableSequenceCollection.Empty)
                .Also(sequences => sequences.Add(pattern, value))
        };
        return this;
    }

    public ResourceDescriptor Build()
        => Descriptor;
}
