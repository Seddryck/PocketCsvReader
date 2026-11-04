using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;
public interface IProfile
{
    SchemaDescriptor? Schema { get; }
    ResourceDescriptor? Resource { get; }
    RuntimeParsersDescriptor? Parsers { get; }
    IReadOnlyList<ReaderFilter> Filters => Array.Empty<ReaderFilter>();
    int Skip => 0;
    int? Take => null;
    bool TrackPosition => false;
    StreamInitializationOptions StreamInitialization => StreamInitializationOptions.Default;
}
