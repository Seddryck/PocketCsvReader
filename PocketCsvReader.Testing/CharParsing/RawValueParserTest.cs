using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader.Testing.CharParsing;
public class RawValueParserTest
{
    [Test]
    [TestCase("foo;")]
    [TestCase("foobar;")]
    public void Parse_FieldWithDelimiter_Parsed(string buffer)
    {
        var context = new Mock<IParserContext>(MockBehavior.Strict);
        context.Setup(x => x.EndValue(It.IsAny<int>()));
        context.SetupGet(x => x.Escaping).Returns(false);

        var parser = new RawParser(context.Object, Mock.Of<IParserStateController>(), "\r\n", ';');
        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        context.Verify(x => x.EndValue(buffer.Length-2), Times.Once);
    }

    [Test]
    [TestCase("f\\oo;", 1)]
    [TestCase("f\\oo\\bar;", 2)]
    public void Parse_FieldWithEscaper_EscapeSessionStarted(string buffer, int escapeCount)
    {
        var context = new Mock<IParserContext>(MockBehavior.Loose);
        context.Setup(x => x.StartEscaping());
        context.SetupGet(x => x.Escaping).Returns(false);

        var parser = new RawParser(context.Object, Mock.Of<IParserStateController>(), "\r\n", ';', '\\');
        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        context.Verify(x => x.StartEscaping(), Times.Exactly(escapeCount));
        context.Verify(x => x.EndValue(buffer.Length - 2), Times.Once);
    }

    [Test]
    [TestCase(@"\\;", 1)]
    [TestCase(@"\\\\;", 2)]
    public void Parse_FieldWithEscaper_EscapeSessionEnded(string buffer, int escapeCount)
    {
        var context = new Mock<IParserContext>(MockBehavior.Loose);
        context.Setup(x => x.EndEscaping());
        var escapes = new Queue<bool>([false, true, false, true, false]);
        context.SetupGet(x => x.Escaping).Returns(escapes.Dequeue);

        var parser = new RawParser(context.Object, Mock.Of<IParserStateController>(), "\r\n", ';', '\\');
        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        context.Verify(x => x.EndEscaping(), Times.Exactly(escapeCount));
    }
}
