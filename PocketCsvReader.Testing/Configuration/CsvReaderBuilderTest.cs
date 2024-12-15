using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing.Configuration;
public class CsvReaderBuilderTest
{
    [Test]
    public void WithDescriptor_ShouldSetDescriptor()
    {
        var builder = new CsvReaderBuilder().WithDialectDescriptor
        (
            (desc) => desc
                        .WithDelimiter(Delimiter.Tab)
                        .WithLineTerminator(LineTerminator.LineFeed)
        );
        var reader = builder.Build();
        Assert.That(reader.Profile.Descriptor.Delimiter, Is.EqualTo('\t'));
        Assert.That(reader.Profile.Descriptor.LineTerminator, Is.EqualTo("\n"));
    }

}
