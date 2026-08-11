using NexusForever.WorldServer.Command;

namespace NexusForever.WorldServer.Tests.Command
{
    public sealed class CommandTokenizerTests
    {
        [Fact]
        public void TryTokenize_CollapsesWhitespaceOutsideQuotes()
        {
            bool result = CommandTokenizer.TryTokenize(
                " \tcommand\u2003  first\r\nsecond ",
                out string[] tokens);

            Assert.True(result);
            Assert.Equal(["command", "first", "second"], tokens);
        }

        [Fact]
        public void TryTokenize_PreservesWhitespaceInsideQuotedToken()
        {
            bool result = CommandTokenizer.TryTokenize(
                "command \"hello  \twide\r\nworld\"",
                out string[] tokens);

            Assert.True(result);
            Assert.Equal(["command", "\"hello  \twide\r\nworld\""], tokens);
        }

        [Fact]
        public void TryTokenize_PreservesExplicitEmptyQuotedToken()
        {
            bool result = CommandTokenizer.TryTokenize("command \"\"", out string[] tokens);

            Assert.True(result);
            Assert.Equal(["command", "\"\""], tokens);
        }

        [Fact]
        public void TryTokenize_PreservesRecognisedEscapesForStringConverter()
        {
            bool result = CommandTokenizer.TryTokenize(
                "command \"say \\\"hello\\\" at C:\\\\temp\"",
                out string[] tokens);

            Assert.True(result);
            Assert.Equal(["command", "\"say \\\"hello\\\" at C:\\\\temp\""], tokens);
        }

        [Fact]
        public void TryTokenize_PreservesUnknownEscapeSequence()
        {
            bool result = CommandTokenizer.TryTokenize("command \"keep \\q\"", out string[] tokens);

            Assert.True(result);
            Assert.Equal(["command", "\"keep \\q\""], tokens);
        }

        [Fact]
        public void TryTokenize_TreatsMidTokenQuoteAsLiteral()
        {
            bool result = CommandTokenizer.TryTokenize("command ab\"cd", out string[] tokens);

            Assert.True(result);
            Assert.Equal(["command", "ab\"cd"], tokens);
        }

        [Fact]
        public void TryTokenize_UnclosedQuotedTokenFails()
        {
            bool result = CommandTokenizer.TryTokenize("command \"unfinished", out string[] tokens);

            Assert.False(result);
            Assert.Empty(tokens);
        }

        [Fact]
        public void TryTokenize_NonWhitespaceAfterClosingQuoteFails()
        {
            bool result = CommandTokenizer.TryTokenize("command \"value\"suffix", out string[] tokens);

            Assert.False(result);
            Assert.Empty(tokens);
        }

        [Fact]
        public void TryTokenize_WhitespaceOnlyProducesNoTokens()
        {
            bool result = CommandTokenizer.TryTokenize(" \t\r\n\u2003", out string[] tokens);

            Assert.True(result);
            Assert.Empty(tokens);
        }
    }
}
