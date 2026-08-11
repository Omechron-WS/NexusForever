using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Options;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static;
using NexusForever.Game.Static.RBAC;
using NexusForever.WorldServer.Command;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Handler;
using NexusForever.WorldServer.Command.Static;
using NexusForever.WorldServer.Web.Configuration;

namespace NexusForever.WorldServer.Tests.Command
{
    public sealed class CommandHelpTests
    {
        [Fact]
        public void HandleHelp_RootCategoriesAreDistinctOrderedAndPermissionFiltered()
        {
            var firstCategory  = BuildCategory<FirstRootCategory>();
            var secondCategory = BuildCategory<SecondRootCategory>();
            var deniedCategory = BuildCategory<DeniedRootCategory>();
            CommandManager manager = CreateManager(
                ("zulu", firstCategory),
                ("alpha", firstCategory),
                ("beta", secondCategory),
                ("echo", secondCategory),
                ("aardvark", deniedCategory),
                ("hidden", deniedCategory));
            var context = new TestCommandContext(
                Permission.Help,
                Permission.Account,
                Permission.Realm);

            try
            {
                manager.HandleHelp(context, new ParameterQueue([]));
            }
            finally
            {
                manager.Shutdown();
            }

            Assert.Empty(context.Errors);
            string message = Assert.Single(context.Messages);
            Assert.Equal(
                [
                    "Category: zulu, alpha - First accessible category.",
                    "Category: beta, echo - Second accessible category."
                ],
                GetHelpLines(message, "Category: "));
        }

        [Fact]
        public void GetHelp_NestedCommandsAreDistinctOrderedAndPermissionFiltered()
        {
            DetailedCategory category = BuildCategory<DetailedCategory>();
            var context = new TestCommandContext(
                Permission.Help,
                Permission.Account,
                Permission.Realm);
            var builder = new StringBuilder();

            category.GetHelp(builder, context, true);

            string help = builder.ToString();
            Assert.Equal(
                [
                    "detailed - Detailed category.",
                    "Command: zulu - First accessible command.",
                    "alpha - First accessible command.",
                    "Command: beta - Second accessible command.",
                    "echo - Second accessible command."
                ],
                help.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
            Assert.DoesNotContain("aardvark", help);
            Assert.DoesNotContain("hidden", help);
        }

        [Theory]
        [InlineData("help")]
        [InlineData("h")]
        [InlineData("?")]
        public void HandleCommand_HelpAliasWithoutPermissionReturnsGenericError(string alias)
        {
            HelpCommandCategory helpCategory = BuildCategory<HelpCommandCategory>();
            CommandManager manager = CreateManager(
                ("help", helpCategory),
                ("h", helpCategory),
                ("?", helpCategory));
            var context = new TestCommandContext();

            try
            {
                manager.HandleCommand(context, alias);
            }
            finally
            {
                manager.Shutdown();
            }

            Assert.Empty(context.Messages);
            Assert.Equal(
                ["Unable to invoke command, it's either an invalid command or you don't have permission to access it!"],
                context.Errors);
        }

        private static T BuildCategory<T>() where T : CommandCategory
        {
            CommandAttribute attribute = typeof(T).GetCustomAttribute<CommandAttribute>();
            if (attribute == null)
                throw new InvalidOperationException($"Missing command attribute on {typeof(T).Name}.");

            var category = (T)Activator.CreateInstance(typeof(T), nonPublic: true);
            category.Build(attribute);
            return category;
        }

        private static CommandManager CreateManager(
            params (string Alias, ICommandHandler Handler)[] aliases)
        {
            var manager = new CommandManager(Options.Create(new WebSocketCommandOptions
            {
                MaximumPendingCommands = 1
            }));
            var builder = ImmutableDictionary.CreateBuilder<string, ICommandHandler>(
                StringComparer.InvariantCultureIgnoreCase);
            foreach ((string alias, ICommandHandler handler) in aliases)
                builder.Add(alias, handler);

            FieldInfo field = typeof(CommandManager).GetField(
                "handlers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException("Missing CommandManager handler registry.");

            field.SetValue(manager, builder.ToImmutable());
            return manager;
        }

        private static string[] GetHelpLines(string message, string prefix)
        {
            return message
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
                .ToArray();
        }

        [Command(Permission.Account, "First accessible category.", "zulu", "alpha")]
        private sealed class FirstRootCategory : CommandCategory
        {
        }

        [Command(Permission.Realm, "Second accessible category.", "beta", "echo")]
        private sealed class SecondRootCategory : CommandCategory
        {
        }

        [Command(Permission.RealmShutdown, "Denied category.", "aardvark", "hidden")]
        private sealed class DeniedRootCategory : CommandCategory
        {
        }

        [Command(Permission.Help, "Detailed category.", "detailed")]
        private sealed class DetailedCategory : CommandCategory
        {
            [Command(Permission.Account, "First accessible command.", "zulu", "alpha")]
            public void HandleFirst(ICommandContext context)
            {
            }

            [Command(Permission.Realm, "Second accessible command.", "beta", "echo")]
            public void HandleSecond(ICommandContext context)
            {
            }

            [Command(Permission.RealmShutdown, "Denied command.", "aardvark", "hidden")]
            public void HandleDenied(ICommandContext context)
            {
            }
        }

        private sealed class TestCommandContext : ICommandContext
        {
            public IWorldEntity Invoker { get; }
            public IWorldEntity Target { get; }

            public Language Language { get; } = Language.English;
            public ImmutableHashSet<Permission> Permissions { get; }

            public List<string> Messages { get; } = [];
            public List<string> Errors { get; } = [];

            public TestCommandContext(params Permission[] permissions)
            {
                Permissions = permissions.ToImmutableHashSet();
            }

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
