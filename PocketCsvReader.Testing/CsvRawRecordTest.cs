using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;
public class CsvRawRecordTest
{
    [Test]
    public void GetDataTypeName_WithIndexedSchema_CorrectNames()
    {
        var schema = new SchemaDescriptorBuilder()
                            .Indexed()   
                            .WithNumericField<decimal>((f) => f.WithDataSourceTypeName("number"))
                            .WithField<string>((f) => f.WithDataSourceTypeName("string"))
                            .Build();
        var profile = new CsvProfile(new DialectDescriptor(), schema);
        var rawRecord = new CsvRawRecord(profile);
        Assert.That(rawRecord.GetDataTypeName(0), Is.EqualTo("number"));
        Assert.That(rawRecord.GetDataTypeName(1), Is.EqualTo("string"));
    }
}
