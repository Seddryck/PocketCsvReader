using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader.Testing.CharParsing;
public class ValueParserTest
{
    [Test]
    [TestCase("foo;")]
    [TestCase("foobar;")]
    public void Parse_RawField_Parsed(string buffer)
    {
        var context = new Mock<IParserContext>(MockBehavior.Loose);
        context.SetupGet(x => x.Escaping).Returns(false);
        var controller = new Mock<IParserStateController>(MockBehavior.Loose);

        var parser = new ValueParser(context.Object, controller.Object, "\r\n", ';', '\'');
        parser.Parse(buffer[0], 0);

        context.Verify(x => x.StartValue(0, false), Times.Once);
        controller.Verify(x => x.SwitchToRaw(), Times.Once);
    }

    [Test]
    [TestCase("'foo';")]
    [TestCase("'foobar';")]
    public void Parse_QuotedField_Parsed(string buffer)
    {
        var context = new Mock<IParserContext>(MockBehavior.Loose);
        context.SetupGet(x => x.Escaping).Returns(false);
        var controller = new Mock<IParserStateController>(MockBehavior.Loose);

        var parser = new ValueParser(context.Object, controller.Object, "\r\n", ';', '\'');
        parser.Parse(buffer[0], 0);

        context.Verify(x => x.StartValue(0, true), Times.Once);
        controller.Verify(x => x.SwitchToQuoted(), Times.Once);
    }
}
