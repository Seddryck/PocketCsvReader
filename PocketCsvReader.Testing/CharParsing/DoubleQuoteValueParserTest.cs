using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader.Testing.CharParsing;
public class DoubleQuoteValueParserTest
{
    [Test]
    [TestCase("'f''oo';")]
    [TestCase("'f''oobar';")]
    public void Parse_FieldWithDelimiter_Parsed(string buffer)
    {
        var context = new Mock<IParserContext>(MockBehavior.Loose);
        context.SetupGet(x => x.Escaping).Returns(false);

        var parser = new DoubleQuoteParser(
                context.Object, Mock.Of<IParserStateController>(), ';', "\r\n", '\'');
        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        context.Verify(x => x.EndValue(buffer.Length-3), Times.Once);
    }

    [Test]
    [TestCase(@"'0''2';")]
    public void Parse_FieldWithEscaper_EscapeSessionStarted(string buffer)
    {
        var context = new Mock<IParserContext>(MockBehavior.Loose);
        var doublings = new Queue<bool>([false, false, true, false, false, true]);
        var completes = new Queue<bool>([false, false, true, false, false, true]);
        context.SetupGet(x => x.Doubling).Returns(doublings.Dequeue);
        context.SetupGet(x => x.IsComplete).Returns(completes.Dequeue);

        var parser = new DoubleQuoteParser(
                context.Object, Mock.Of<IParserStateController>(), ';', "\r\n", '\'');

        for (int i = 1; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        context.Verify(x => x.StartDoubling(), Times.Exactly(2));
        context.Verify(x => x.EndDoubling(), Times.Exactly(1));
        context.Verify(x => x.RemoveDoubling(), Times.Exactly(1));
        context.Verify(x => x.EndValue(It.IsAny<int>()), Times.Exactly(2));
        context.Verify(x => x.EndValue(buffer.Length - 3), Times.Once);
    }
}
