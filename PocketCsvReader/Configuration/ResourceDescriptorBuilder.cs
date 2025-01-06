using System;
using System.Collections.Generic;
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
    public ResourceDescriptor Build()
        => Descriptor;
}
