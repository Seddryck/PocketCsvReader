using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader.Testing.CharParsing;
public class QuotedValueParserTest
{
    [Test]
    [TestCase("foo';")]
    [TestCase("foobar';")]
    public void Parse_FieldWithDelimiter_Parsed(string buffer)
    {
        var context = new Mock<IParserContext>(MockBehavior.Loose);
        context.SetupGet(x => x.Escaping).Returns(false);

        var parser = new QuotedParser(context.Object, Mock.Of<IParserStateController>(), ';', "\r\n", '\'');
        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        context.Verify(x => x.EndValue(buffer.Length-3), Times.Once);
    }

    [Test]
    [TestCase(@"0\x2';")]
    [TestCase(@"0\;2';")]
    [TestCase(@"0\'2';")]
    public void Parse_FieldWithEscaper_EscapeSessionStarted(string buffer)
    {
        var context = new Mock<IParserContext>(MockBehavior.Loose);
        var escapes = new Queue<bool>([false, false, true, false, false, false]);
        context.SetupGet(x => x.Escaping).Returns(escapes.Dequeue);

        var parser = new QuotedParser(context.Object, Mock.Of<IParserStateController>(), '\'', "\r\n", '\\');
        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        context.Verify(x => x.StartEscaping(), Times.Exactly(1));
        context.Verify(x => x.EndEscaping(), Times.Exactly(1));
        context.Verify(x => x.EndValue(buffer.Length - 3), Times.Once);
    }

    [Test]
    [TestCase(@"0\x2\x';")]
    [TestCase(@"0\;2\;';")]
    [TestCase(@"0\'2\'';")]
    public void Parse_FieldWithManyEscapers_EscapeSessionStarted(string buffer)
    {
        var context = new Mock<IParserContext>(MockBehavior.Loose);
        var escapes = new Queue<bool>([false, false, true, false, false, true, false, false]);
        context.SetupGet(x => x.Escaping).Returns(escapes.Dequeue);

        var parser = new QuotedParser(context.Object, Mock.Of<IParserStateController>(), '\'', "\r\n", '\\');
        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        context.Verify(x => x.StartEscaping(), Times.Exactly(2));
        context.Verify(x => x.EndEscaping(), Times.Exactly(2));
        context.Verify(x => x.EndValue(buffer.Length - 3), Times.Once);
    }
}
