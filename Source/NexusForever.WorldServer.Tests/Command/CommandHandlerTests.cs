using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static;
using NexusForever.Game.Static.RBAC;
using NexusForever.WorldServer.Command;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Convert;
using NexusForever.WorldServer.Command.Static;

namespace NexusForever.WorldServer.Tests.Command
{
    public sealed class CommandHandlerTests
    {
        private const string CommandFailureMessage = "Something went wrong :(";
        private const string ConverterExceptionDetail = "sensitive converter exception detail";
        private const string HandlerExceptionDetail = "sensitive handler exception detail";

        [Fact]
        public void Invoke_ExactZeroParameterCommand_InvokesHandler()
        {
            var target = new InvocationCategory();
            var context = new TestCommandContext();
            CommandHandler handler = CreateHandler(target, nameof(InvocationCategory.HandleNoParameters));

            CommandResult result = handler.Invoke(context, new ParameterQueue([]));

            Assert.Equal(CommandResult.Ok, result);
            Assert.Equal(1, target.InvocationCount);
            Assert.Empty(context.Errors);
        }

        [Fact]
        public void Invoke_ZeroParameterCommandWithSurplusToken_DoesNotInvokeHandler()
        {
            var target = new InvocationCategory();
            CommandHandler handler = CreateHandler(target, nameof(InvocationCategory.HandleNoParameters));

            CommandResult result = handler.Invoke(
                new TestCommandContext(),
                new ParameterQueue(["unexpected"]));

            Assert.Equal(CommandResult.InvalidParameters, result);
            Assert.Equal(0, target.InvocationCount);
        }

        [Fact]
        public void Invoke_ExactQuotedString_InvokesHandlerWithCompleteValue()
        {
            var target = new InvocationCategory();
            CommandHandler handler = CreateHandler(
                target,
                nameof(InvocationCategory.HandleString),
                new CommandHandler.CommandParameter(typeof(string), new StringParameterConverter(), false));

            CommandResult result = handler.Invoke(
                new TestCommandContext(),
                new ParameterQueue(["\"hello wide world\""]));

            Assert.Equal(CommandResult.Ok, result);
            Assert.Equal(1, target.InvocationCount);
            Assert.Equal("hello wide world", target.StringValue);
        }

        [Fact]
        public void Invoke_QuotedStringWithSurplusToken_DoesNotInvokeHandler()
        {
            var target = new InvocationCategory();
            CommandHandler handler = CreateHandler(
                target,
                nameof(InvocationCategory.HandleString),
                new CommandHandler.CommandParameter(typeof(string), new StringParameterConverter(), false));

            CommandResult result = handler.Invoke(
                new TestCommandContext(),
                new ParameterQueue(["\"hello wide world\"", "unexpected"]));

            Assert.Equal(CommandResult.InvalidParameters, result);
            Assert.Equal(0, target.InvocationCount);
            Assert.Null(target.StringValue);
        }

        [Fact]
        public void Invoke_ExactVector3_InvokesHandlerWithCompleteValue()
        {
            var target = new InvocationCategory();
            CommandHandler handler = CreateHandler(
                target,
                nameof(InvocationCategory.HandleVector),
                new CommandHandler.CommandParameter(typeof(Vector3), new Vector3ParameterConverter(), false));

            CommandResult result = handler.Invoke(
                new TestCommandContext(),
                new ParameterQueue(["1", "2", "3"]));

            Assert.Equal(CommandResult.Ok, result);
            Assert.Equal(1, target.InvocationCount);
            Assert.True(target.VectorValue.HasValue);
            Assert.Equal(new Vector3(1f, 2f, 3f), target.VectorValue.Value);
        }

        [Fact]
        public void Invoke_Vector3WithSurplusToken_DoesNotInvokeHandler()
        {
            var target = new InvocationCategory();
            CommandHandler handler = CreateHandler(
                target,
                nameof(InvocationCategory.HandleVector),
                new CommandHandler.CommandParameter(typeof(Vector3), new Vector3ParameterConverter(), false));

            CommandResult result = handler.Invoke(
                new TestCommandContext(),
                new ParameterQueue(["1", "2", "3", "unexpected"]));

            Assert.Equal(CommandResult.InvalidParameters, result);
            Assert.Equal(0, target.InvocationCount);
            Assert.Null(target.VectorValue);
        }

        [Fact]
        public void Invoke_ThrowingHandler_SendsOneGenericErrorAndSubsequentHandlerRuns()
        {
            var target = new InvocationCategory();
            var context = new TestCommandContext();
            CommandHandler throwingHandler = CreateHandler(target, nameof(InvocationCategory.HandleThrowing));
            CommandHandler succeedingHandler = CreateHandler(target, nameof(InvocationCategory.HandleNoParameters));

            CommandResult failureResult = throwingHandler.Invoke(context, new ParameterQueue([]));
            CommandResult succeedingResult = succeedingHandler.Invoke(context, new ParameterQueue([]));

            Assert.Equal(CommandResult.Ok, failureResult);
            Assert.Equal(CommandResult.Ok, succeedingResult);
            Assert.Equal(1, target.InvocationCount);
            Assert.Equal([CommandFailureMessage], context.Errors);
            Assert.DoesNotContain(HandlerExceptionDetail, context.Errors[0]);
        }

        [Fact]
        public void Invoke_ThrowingConverter_SendsOneGenericErrorWithoutInvokingHandler()
        {
            var target = new InvocationCategory();
            var context = new TestCommandContext();
            CommandHandler handler = CreateHandler(
                target,
                nameof(InvocationCategory.HandleString),
                new CommandHandler.CommandParameter(typeof(string), new ThrowingParameterConverter(), false));

            CommandResult result = handler.Invoke(context, new ParameterQueue(["value"]));

            Assert.Equal(CommandResult.Ok, result);
            Assert.Equal(0, target.InvocationCount);
            Assert.Null(target.StringValue);
            Assert.Equal([CommandFailureMessage], context.Errors);
            Assert.DoesNotContain(ConverterExceptionDetail, context.Errors[0]);
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

        private static void SetField(CommandHandler handler, string fieldName, object value)
        {
            FieldInfo field = typeof(CommandHandler).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException($"Missing CommandHandler field {fieldName}.");

            field.SetValue(handler, value);
        }

        private sealed class InvocationCategory : CommandCategory
        {
            public int InvocationCount { get; private set; }
            public string StringValue { get; private set; }
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

            public void HandleVector(ICommandContext context, Vector3 value)
            {
                InvocationCount++;
                VectorValue = value;
            }

            public void HandleThrowing(ICommandContext context)
            {
                throw new InvalidOperationException(HandlerExceptionDetail);
            }
        }

        private sealed class ThrowingParameterConverter : IParameterConvert
        {
            public object Convert(ICommandContext context, ParameterQueue queue)
            {
                throw new InvalidOperationException(ConverterExceptionDetail);
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
