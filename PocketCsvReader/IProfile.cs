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
}
