using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;

public record ResourceDescriptor
(
    string? Encoding = null,
    string? Compression = null,
    ImmutableSequenceCollection? Sequences = null
)
{ }
