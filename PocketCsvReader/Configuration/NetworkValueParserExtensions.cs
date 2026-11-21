using System;
using System.Net;
using System.Net.NetworkInformation;

namespace PocketCsvReader.Configuration;

/// <summary>Registers the built-in .NET parsers for network values.</summary>
public static class NetworkValueParserExtensions
{
    /// <summary>Enables typed parsing of IP addresses, CIDR networks, and MAC addresses.</summary>
    /// <remarks>Explicit field and runtime parsers take precedence over these defaults.</remarks>
    public static RuntimeParsersDescriptorBuilder UseNetworkValues(this RuntimeParsersDescriptorBuilder options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options
            .WithDefaultParser<IPAddress>(IPAddress.Parse)
            .WithDefaultParser<IPNetwork>(IPNetwork.Parse)
            .WithDefaultParser<PhysicalAddress>(PhysicalAddress.Parse);
    }
}
