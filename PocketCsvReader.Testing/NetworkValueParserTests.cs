using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class NetworkValueParserTests
{
    [TestCase("192.0.2.42", "192.0.2.0/24", "00:1A:2B:3C:4D:5E")]
    [TestCase("2001:db8::1", "2001:db8::/32", "00-1A-2B-3C-4D-5E")]
    public void ReadsAndValidatesNetworkColumns(string ip, string cidr, string mac)
    {
        using var reader = ReadRow($"{ip},{cidr},{mac}");

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<IPAddress>("ip").ToString(), Is.EqualTo(ip));
        Assert.That(reader.GetFieldValue<IPNetwork>("cidr").ToString(), Is.EqualTo(cidr));
        Assert.That(reader.GetFieldValue<PhysicalAddress>("mac").ToString(), Is.EqualTo("001A2B3C4D5E"));
        Assert.That(reader.GetValue("cidr"), Is.EqualTo(IPNetwork.Parse(cidr)));
        Assert.That(reader.IsValid("ip"), Is.True);
        Assert.That(reader.IsValid("cidr"), Is.True);
        Assert.That(reader.IsValid("mac"), Is.True);
    }

    [Test]
    public void RejectsMalformedNetworkColumns()
    {
        using var reader = ReadRow("256.0.0.1,2001:db8::/129,00:1A:2B:3C:4D:GG");

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid("ip"), Is.False);
        Assert.That(reader.IsValid("cidr"), Is.False);
        Assert.That(reader.IsValid("mac"), Is.False);
        Assert.That(() => reader.GetFieldValue<IPAddress>("ip"), Throws.TypeOf<FormatException>());
        Assert.That(() => reader.GetFieldValue<IPNetwork>("cidr"), Throws.TypeOf<FormatException>());
        Assert.That(() => reader.GetFieldValue<PhysicalAddress>("mac"), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void PreservesNullHandling()
    {
        using var reader = ReadRow("NULL,NULL,NULL");

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<IPAddress>("ip"), Is.Null);
        Assert.That(reader.GetFieldValue<IPNetwork?>("cidr"), Is.Null);
        Assert.That(reader.GetFieldValue<PhysicalAddress>("mac"), Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ExplicitParsersOverrideNetworkDefaults(bool registerDefaultsFirst)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithoutHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Indexed()
                .WithField<IPAddress>(f => f.WithParser((ReadOnlySpan<char> _) => IPAddress.IPv6Loopback))
                .WithField<IPAddress>())
            .WithParsers(p =>
            {
                if (registerDefaultsFirst)
                    p.UseNetworkValues();
                p.WithParser((ReadOnlySpan<char> _) => IPAddress.Loopback);
                if (!registerDefaultsFirst)
                    p.UseNetworkValues();
                return p;
            })
            .Build();
        using var stream = new MemoryStream("field,runtime\n"u8.ToArray());
        using var reader = csv.ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<IPAddress>(0), Is.EqualTo(IPAddress.IPv6Loopback));
        Assert.That(reader.GetFieldValue<IPAddress>(1), Is.EqualTo(IPAddress.Loopback));
    }

    private static CsvDataReader ReadRow(string row)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n").WithNullSequence("NULL"))
            .WithSchema(s => s.Named()
                .WithField<IPAddress>("ip")
                .WithField<IPNetwork?>("cidr")
                .WithField<PhysicalAddress>("mac"))
            .WithParsers(p => p.UseNetworkValues())
            .Build();
        return csv.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes($"ip,cidr,mac\n{row}\n")));
    }
}
