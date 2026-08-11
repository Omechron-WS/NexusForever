using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using Microsoft.Extensions.Options;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static;
using NexusForever.Game.Static.RBAC;
using NexusForever.WorldServer.Command;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Convert;
using NexusForever.WorldServer.Web.Configuration;

namespace NexusForever.WorldServer.Tests.Command
{
    public sealed class CommandManagerParsingTests
    {
        private const string CommandFailureMessage = "Something went wrong :(";
        private const string InvalidCommandMessage = "Unable to invoke command, it's either an invalid command or you don't have permission to access it!";
        private const string InvalidParametersMessage = "Invalid parameters supplied to command, see help for more information!";

        [Fact]
        public void HandleCommand_WhitespaceOutsideQuotesIsIgnoredAndQuotedWhitespaceIsPreserved()
        {
            using TestHarness harness = TestHarness.CreateString();

            harness.Handle(" \ttest\u2003  \"hello  \twide\" \r\n");

            Assert.Equal(1, harness.Target.InvocationCount);
            Assert.Equal("hello  \twide", harness.Target.StringValue);
            Assert.Empty(harness.Context.Errors);
        }

        [Fact]
        public void HandleCommand_RecognisedEscapesAreDecodedAndUnknownEscapesArePreserved()
        {
            using TestHarness harness = TestHarness.CreateString();

            harness.Handle("test \"say \\\"hello\\\" at C:\\\\temp and \\q\"");

            Assert.Equal(1, harness.Target.InvocationCount);
            Assert.Equal("say \"hello\" at C:\\temp and \\q", harness.Target.StringValue);
            Assert.Empty(harness.Context.Errors);
        }

        [Fact]
        public void HandleCommand_ExplicitEmptyQuotedStringIsPassedToHandler()
        {
            using TestHarness harness = TestHarness.CreateString();

            harness.Handle("test \"\"");

            Assert.Equal(1, harness.Target.InvocationCount);
            Assert.Equal(string.Empty, harness.Target.StringValue);
            Assert.Empty(harness.Context.Errors);
        }

        [Fact]
        public void HandleCommand_MidTokenQuoteRemainsLiteral()
        {
            using TestHarness harness = TestHarness.CreateString();

            harness.Handle("test ab\"cd");

            Assert.Equal(1, harness.Target.InvocationCount);
            Assert.Equal("ab\"cd", harness.Target.StringValue);
            Assert.Empty(harness.Context.Errors);
        }

        [Theory]
        [InlineData("test \"unfinished")]
        [InlineData("test \"value\"suffix")]
        public void HandleCommand_MalformedQuotedStringDoesNotInvokeHandler(string commandText)
        {
            using TestHarness harness = TestHarness.CreateString();

            harness.Handle(commandText);

            Assert.Equal(0, harness.Target.InvocationCount);
            Assert.Equal([InvalidParametersMessage], harness.Context.Errors);
        }

        [Fact]
        public void HandleCommand_QuotedNumericParameterIsNotAccepted()
        {
            using TestHarness harness = TestHarness.CreateInt();

            harness.Handle("test \"42\"");

            Assert.Equal(0, harness.Target.InvocationCount);
            Assert.Equal([CommandFailureMessage], harness.Context.Errors);
        }

        [Fact]
        public void HandleCommand_QuotedCategoryIsNotAccepted()
        {
            using TestHarness harness = TestHarness.CreateInt();

            harness.Handle("\"test\" 42");

            Assert.Equal(0, harness.Target.InvocationCount);
            Assert.Equal([InvalidCommandMessage], harness.Context.Errors);
        }

        [Fact]
        public void HandleCommand_RepeatedWhitespaceBetweenNumericParametersIsIgnored()
        {
            using TestHarness harness = TestHarness.CreateInt();

            harness.Handle("test   42");

            Assert.Equal(1, harness.Target.InvocationCount);
            Assert.Equal(42, harness.Target.IntValue);
            Assert.Empty(harness.Context.Errors);
        }

        [Fact]
        public void HandleCommand_Vector3ConverterAcceptsMixedRepeatedUnicodeWhitespace()
        {
            using TestHarness harness = TestHarness.CreateVector();

            harness.Handle("test  1\u2003\t2\r\n  3");

            Assert.Equal(1, harness.Target.InvocationCount);
            Assert.True(harness.Target.VectorValue.HasValue);
            Assert.Equal(new Vector3(1f, 2f, 3f), harness.Target.VectorValue.Value);
            Assert.Empty(harness.Context.Errors);
        }

        [Fact]
        public void HandleCommand_OptionalStringDistinguishesOmissionFromExplicitEmpty()
        {
            using TestHarness omittedHarness = TestHarness.CreateOptionalString();
            using TestHarness emptyHarness = TestHarness.CreateOptionalString();

            omittedHarness.Handle("test");
            emptyHarness.Handle("test \"\"");

            Assert.Equal(1, omittedHarness.Target.InvocationCount);
            Assert.Null(omittedHarness.Target.StringValue);
            Assert.Empty(omittedHarness.Context.Errors);

            Assert.Equal(1, emptyHarness.Target.InvocationCount);
            Assert.Equal(string.Empty, emptyHarness.Target.StringValue);
            Assert.Empty(emptyHarness.Context.Errors);
        }

        [Fact]
        public void HandleCommand_TrailingWhitespaceIsNotASurplusParameter()
        {
            using TestHarness harness = TestHarness.CreateNoParameters();

            harness.Handle("test \t\r\n");

            Assert.Equal(1, harness.Target.InvocationCount);
            Assert.Empty(harness.Context.Errors);
        }

        [Fact]
        public void HandleCommand_RealSurplusParameterRemainsInvalid()
        {
            using TestHarness harness = TestHarness.CreateString();

            harness.Handle("test value unexpected");

            Assert.Equal(0, harness.Target.InvocationCount);
            Assert.Equal([InvalidParametersMessage], harness.Context.Errors);
        }

        private sealed class TestHarness : IDisposable
        {
            public InvocationCategory Target { get; }
            public TestCommandContext Context { get; } = new();

            private readonly CommandManager manager;

            private TestHarness(string methodName, params CommandHandler.CommandParameter[] parameters)
            {
                Target = new InvocationCategory();
                CommandHandler handler = CreateHandler(Target, methodName, parameters);
                manager = CreateManager(handler);
            }

            public static TestHarness CreateNoParameters()
            {
                return new TestHarness(nameof(InvocationCategory.HandleNoParameters));
            }

            public static TestHarness CreateString()
            {
                return new TestHarness(
                    nameof(InvocationCategory.HandleString),
                    new CommandHandler.CommandParameter(typeof(string), new StringParameterConverter(), false));
            }

            public static TestHarness CreateInt()
            {
                return new TestHarness(
                    nameof(InvocationCategory.HandleInt),
                    new CommandHandler.CommandParameter(typeof(int), new IntParameterConverter(), false));
            }

            public static TestHarness CreateVector()
            {
                return new TestHarness(
                    nameof(InvocationCategory.HandleVector),
                    new CommandHandler.CommandParameter(typeof(Vector3), new Vector3ParameterConverter(), false));
            }

            public static TestHarness CreateOptionalString()
            {
                return new TestHarness(
                    nameof(InvocationCategory.HandleOptionalString),
                    new CommandHandler.CommandParameter(typeof(string), new StringParameterConverter(), true));
            }

            public void Handle(string commandText)
            {
                manager.HandleCommand(Context, commandText);
            }

            public void Dispose()
            {
                manager.Shutdown();
            }
        }

        private static CommandManager CreateManager(ICommandHandler handler)
        {
            var manager = new CommandManager(Options.Create(new WebSocketCommandOptions
            {
                MaximumPendingCommands = 1
            }));
            var handlers = ImmutableDictionary.CreateBuilder<string, ICommandHandler>(
                StringComparer.InvariantCultureIgnoreCase);
            handlers.Add("test", handler);
            SetField(manager, "handlers", handlers.ToImmutable());
            return manager;
        }

        private static CommandHandler CreateHandler(
            InvocationCategory target,
            string methodName,
            params CommandHandler.CommandParameter[] parameters)
        {
            MethodInfo method = typeof(InvocationCategory).GetMethod(methodName);
            if (method == null)
                throw new InvalidOperationException($"Missing test command method {methodName}.");

            var handler = new CommandHandler();
            SetField(handler, "methodContainer", new MethodContainer(target, method));
            SetField(handler, "parameters", parameters.ToImmutableList());
            return handler;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException($"Missing {target.GetType().Name} field {fieldName}.");

            field.SetValue(target, value);
        }

        private sealed class InvocationCategory : CommandCategory
        {
            public int InvocationCount { get; private set; }
            public string StringValue { get; private set; }
            public int? IntValue { get; private set; }
            public Vector3? VectorValue { get; private set; }

            public void HandleNoParameters(ICommandContext context)
            {
                InvocationCount++;
            }

            public void HandleString(ICommandContext context, string value)
            {
                InvocationCount++;
                StringValue = value;
            }

            public void HandleInt(ICommandContext context, int value)
            {
                InvocationCount++;
                IntValue = value;
            }

            public void HandleVector(ICommandContext context, Vector3 value)
            {
                InvocationCount++;
                VectorValue = value;
            }

            public void HandleOptionalString(ICommandContext context, string value)
            {
                InvocationCount++;
                StringValue = value;
            }
        }

        private sealed class TestCommandContext : ICommandContext
        {
            public IWorldEntity Invoker { get; }
            public IWorldEntity Target { get; }

            public Language Language { get; } = Language.English;
            public ImmutableHashSet<Permission> Permissions { get; } = [];

            public List<string> Messages { get; } = [];
            public List<string> Errors { get; } = [];

            public void SendMessage(string message)
            {
                Messages.Add(message);
            }

            public void SendError(string message)
            {
                Errors.Add(message);
            }

            public T GetTargetOrInvoker<T>() where T : IWorldEntity
            {
                return default;
            }
        }
    }
}
