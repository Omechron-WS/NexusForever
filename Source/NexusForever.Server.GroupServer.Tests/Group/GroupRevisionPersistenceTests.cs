using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.API.Character.Client;
using NexusForever.Database.Group;
using NexusForever.Database.Group.Model;
using NexusForever.Database.Group.Repository;
using NexusForever.Game.Static.Group;
using NexusForever.Network.Internal.Message.Group;
using NexusForever.Server.GroupServer.Character;
using NexusForever.Server.GroupServer.Group;
using NexusForever.Server.GroupServer.Network.Internal;
using GroupEntity = NexusForever.Server.GroupServer.Group.Group;
using GroupIdentity = NexusForever.Server.GroupServer.Identity;

namespace NexusForever.Server.GroupServer.Tests.Group
{
    public class GroupRevisionPersistenceTests
    {
        private const ulong GroupId = 42ul;
        private const ulong InitialRevision = 7ul;
        private const ushort RealmId = 1;
        private const ulong LeaderId = 101ul;
        private const string TestOutboxTable = "a_internal_message";

        [Fact]
        public async Task SetGroupFlagsAsync_ConcurrentContexts_CommitsOnlyWinnerAndRollsBackLoserOutbox()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            using var httpClient = new HttpClient(new UnexpectedHttpMessageHandler())
            {
                BaseAddress = new Uri("http://group-tests.invalid"),
            };
            var commandRecorder = new CommandRecorder();
            await using ServiceProvider provider = CreateProvider(connection, httpClient, commandRecorder);
            await SeedAsync(provider);

            Guid winnerMessageId;
            Guid[] loserMessageIds;
            {
                await using AsyncServiceScope winnerScope = provider.CreateAsyncScope();
                await using AsyncServiceScope loserScope = provider.CreateAsyncScope();

                GroupContext winnerContext = winnerScope.ServiceProvider.GetRequiredService<GroupContext>();
                GroupContext loserContext = loserScope.ServiceProvider.GetRequiredService<GroupContext>();
                GroupEntity winner = await winnerScope.ServiceProvider
                    .GetRequiredService<GroupManager>()
                    .GetGroupAsync(GroupId);
                GroupEntity loser = await loserScope.ServiceProvider
                    .GetRequiredService<GroupManager>()
                    .GetGroupAsync(GroupId);

                Assert.NotNull(winner);
                Assert.NotNull(loser);
                Assert.Equal(InitialRevision, winner.Revision);
                Assert.Equal(InitialRevision, loser.Revision);

                GroupActionResult winnerResult = await winner.SetGroupFlagsAsync(
                    Identity(LeaderId), GroupFlags.JoinRequestOpen);
                GroupActionResult loserResult = await loser.SetGroupFlagsAsync(
                    Identity(LeaderId), GroupFlags.Raid);

                Assert.Equal(GroupActionResult.FlagsSuccess, winnerResult);
                Assert.Equal(GroupActionResult.FlagsSuccess, loserResult);
                Assert.Equal(InitialRevision + 1ul, winner.Revision);
                Assert.Equal(InitialRevision + 1ul, loser.Revision);
                Assert.Equal(InitialRevision, winnerContext.Entry(winner.Model)
                    .Property(model => model.Revision).OriginalValue);
                Assert.Equal(InitialRevision, loserContext.Entry(loser.Model)
                    .Property(model => model.Revision).OriginalValue);

                InternalMessageModel winnerMessage = Assert.Single(AddedMessages(winnerContext));
                InternalMessageModel[] loserMessages = AddedMessages(loserContext);
                Assert.Equal(typeof(GroupFlagsUpdatedMessage).AssemblyQualifiedName, winnerMessage.Type);
                Assert.Equal(2, loserMessages.Length);
                Assert.Contains(loserMessages, message =>
                    message.Type == typeof(GroupFlagsUpdatedMessage).AssemblyQualifiedName);
                Assert.Contains(loserMessages, message =>
                    message.Type == typeof(GroupMaxSizeUpdatedMessage).AssemblyQualifiedName);
                winnerMessageId = winnerMessage.Id;
                loserMessageIds = loserMessages.Select(message => message.Id).ToArray();

                await winnerContext.SaveChangesAsync();
                commandRecorder.Clear();

                DbUpdateConcurrencyException exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                    () => loserContext.SaveChangesAsync());
                var concurrencyEntry = Assert.Single(exception.Entries);
                Assert.Same(loser.Model, Assert.IsType<GroupModel>(concurrencyEntry.Entity));

                // The test-only table name forces the outbox ahead of "group" in EF's modification order.
                // Recording completed commands proves this worst-case default-transaction probe actually
                // executed a loser outbox INSERT before discovering the stale group revision.
                ExecutedCommand[] loserCommands = commandRecorder.Commands.ToArray();
                Assert.NotEmpty(loserCommands);
                string commandSequence = string.Join(
                    "\n/* command boundary */\n",
                    loserCommands.Select(command => command.CommandText));
                string outboxInsert = $"INSERT INTO \"{TestOutboxTable}\"";
                const string groupUpdate = "UPDATE \"group\"";
                int outboxInsertIndex = commandSequence.IndexOf(outboxInsert, StringComparison.OrdinalIgnoreCase);
                int groupUpdateIndex = commandSequence.IndexOf(groupUpdate, StringComparison.OrdinalIgnoreCase);

                Assert.True(outboxInsertIndex >= 0,
                    $"No completed loser outbox INSERT was recorded. Commands:{Environment.NewLine}{commandSequence}");
                Assert.True(groupUpdateIndex > outboxInsertIndex,
                    $"The stale group UPDATE did not execute after the loser outbox INSERT. Commands:{Environment.NewLine}{commandSequence}");
                Assert.Contains(loserCommands, command =>
                    command.CommandText.Contains(outboxInsert, StringComparison.OrdinalIgnoreCase)
                    && command.ParameterValues.Any(value => IsMessageId(value, loserMessageIds)));
            }

            await using AsyncServiceScope verificationScope = provider.CreateAsyncScope();
            GroupContext verificationContext = verificationScope.ServiceProvider.GetRequiredService<GroupContext>();
            GroupModel persistedGroup = await verificationContext.Group
                .AsNoTracking()
                .SingleAsync(group => group.GroupId == GroupId);
            InternalMessageModel[] persistedMessages = await verificationContext.InternalMessage
                .AsNoTracking()
                .ToArrayAsync();

            Assert.Equal(InitialRevision + 1ul, persistedGroup.Revision);
            Assert.Equal(GroupFlags.JoinRequestOpen, persistedGroup.Flags);

            InternalMessageModel persistedMessage = Assert.Single(persistedMessages);
            Assert.Equal(winnerMessageId, persistedMessage.Id);
            Assert.Equal(typeof(GroupFlagsUpdatedMessage).AssemblyQualifiedName, persistedMessage.Type);
            Assert.DoesNotContain(persistedMessages, message => loserMessageIds.Contains(message.Id));
            Assert.DoesNotContain(persistedMessages, message =>
                message.Type == typeof(GroupMaxSizeUpdatedMessage).AssemblyQualifiedName);

            GroupFlagsUpdatedMessage payload = JsonSerializer.Deserialize<GroupFlagsUpdatedMessage>(
                persistedMessage.Payload);
            Assert.NotNull(payload);
            Assert.Equal(InitialRevision + 1ul, payload.Group.Revision);
            Assert.Equal(GroupFlags.JoinRequestOpen, payload.Group.Flags);
        }

        private static ServiceProvider CreateProvider(
            SqliteConnection connection,
            HttpClient httpClient,
            CommandRecorder commandRecorder)
        {
            DbContextOptions<GroupContext> options = new DbContextOptionsBuilder<GroupContext>()
                .UseSqlite(connection)
                .AddInterceptors(commandRecorder)
                .Options;
            var services = new ServiceCollection();
            services.AddScoped<GroupContext>(_ => new OrderedOutboxGroupContext(options));
            services.AddSingleton(new CharacterAPIClient(httpClient));
            services.AddScoped<CharacterRepository>();
            services.AddScoped<GroupRepository>();
            services.AddScoped<InternalMessageRepository>();
            services.AddTransient<OutboxMessagePublisher>();
            services.AddGroup();
            services.AddCharacter();
            return services.BuildServiceProvider();
        }

        private static async Task SeedAsync(ServiceProvider provider)
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            GroupContext context = scope.ServiceProvider.GetRequiredService<GroupContext>();
            await context.Database.EnsureCreatedAsync();

            context.Character.Add(new CharacterModel
            {
                CharacterId  = LeaderId,
                RealmId      = RealmId,
                RealmName    = "TestRealm",
                Name         = "Leader",
                CurrentRealm = RealmId,
            });
            context.Group.Add(new GroupModel
            {
                GroupId           = GroupId,
                Revision          = InitialRevision,
                Flags             = GroupFlags.None,
                LootRule          = LootRule.NeedBeforeGreed,
                LootRuleThreshold = LootRule.NeedBeforeGreed,
                LootThreshold     = LootThreshold.Good,
                LootRuleHarvest   = HarvestLootRule.FirstTagger,
                Leader            = new GroupLeaderModel
                {
                    GroupId     = GroupId,
                    CharacterId = LeaderId,
                    RealmId     = RealmId,
                },
                Members =
                [
                    new GroupMemberModel
                    {
                        GroupId     = GroupId,
                        CharacterId = LeaderId,
                        RealmId     = RealmId,
                        Index       = 1u,
                        Flags       = GroupMemberInfoFlags.GroupAdminFlags,
                    },
                ],
            });
            context.CharacterGroup.Add(new CharacterGroupModel
            {
                CharacterId = LeaderId,
                RealmId     = RealmId,
                GroupId     = GroupId,
                Index       = 0u,
            });

            await context.SaveChangesAsync();
        }

        private static InternalMessageModel[] AddedMessages(GroupContext context)
        {
            return context.ChangeTracker
                .Entries<InternalMessageModel>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity)
                .ToArray();
        }

        private static bool IsMessageId(object value, IReadOnlyCollection<Guid> messageIds)
        {
            if (value is Guid guid)
                return messageIds.Contains(guid);

            if (value is string text && Guid.TryParse(text, out Guid parsedGuid))
                return messageIds.Contains(parsedGuid);

            if (value is byte[] bytes && bytes.Length == 16)
                return messageIds.Contains(new Guid(bytes));

            return false;
        }

        private static GroupIdentity Identity(ulong id)
        {
            return new GroupIdentity
            {
                Id      = id,
                RealmId = RealmId,
            };
        }

        /// <summary>
        /// Test-only model variant that deliberately puts the outbox before the group table in EF's
        /// modification ordering. Production mappings are unchanged.
        /// </summary>
        private sealed class OrderedOutboxGroupContext : GroupContext
        {
            public OrderedOutboxGroupContext(DbContextOptions<GroupContext> options)
                : base(options)
            {
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);
                modelBuilder.Entity<InternalMessageModel>().ToTable(TestOutboxTable);
            }
        }

        private sealed record ExecutedCommand(string CommandText, object[] ParameterValues);

        private sealed class CommandRecorder : DbCommandInterceptor
        {
            private readonly List<ExecutedCommand> commands = [];

            public IReadOnlyList<ExecutedCommand> Commands => commands;

            public void Clear()
            {
                commands.Clear();
            }

            public override DbDataReader ReaderExecuted(
                DbCommand command,
                CommandExecutedEventData eventData,
                DbDataReader result)
            {
                Record(command);
                return result;
            }

            public override ValueTask<DbDataReader> ReaderExecutedAsync(
                DbCommand command,
                CommandExecutedEventData eventData,
                DbDataReader result,
                CancellationToken cancellationToken = default)
            {
                Record(command);
                return ValueTask.FromResult(result);
            }

            public override int NonQueryExecuted(
                DbCommand command,
                CommandExecutedEventData eventData,
                int result)
            {
                Record(command);
                return result;
            }

            public override ValueTask<int> NonQueryExecutedAsync(
                DbCommand command,
                CommandExecutedEventData eventData,
                int result,
                CancellationToken cancellationToken = default)
            {
                Record(command);
                return ValueTask.FromResult(result);
            }

            public override object ScalarExecuted(
                DbCommand command,
                CommandExecutedEventData eventData,
                object result)
            {
                Record(command);
                return result;
            }

            public override ValueTask<object> ScalarExecutedAsync(
                DbCommand command,
                CommandExecutedEventData eventData,
                object result,
                CancellationToken cancellationToken = default)
            {
                Record(command);
                return ValueTask.FromResult(result);
            }

            private void Record(DbCommand command)
            {
                commands.Add(new ExecutedCommand(
                    command.CommandText,
                    command.Parameters
                        .Cast<DbParameter>()
                        .Select(parameter => parameter.Value)
                        .ToArray()));
            }
        }

        private sealed class UnexpectedHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("Character API fallback was not expected in persistence tests.");
            }
        }
    }
}
