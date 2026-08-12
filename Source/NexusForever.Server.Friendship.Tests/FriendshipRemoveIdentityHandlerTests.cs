using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.API.Account.Client;
using NexusForever.API.Character.Client;
using NexusForever.Database.Friendship;
using NexusForever.Database.Friendship.Model;
using NexusForever.Database.Friendship.Repository;
using NexusForever.Game.Static.Friendship;
using NexusForever.Network.Internal.Message.Friendship;
using NexusForever.Server.Friendship.Game.Account;
using NexusForever.Server.Friendship.Game.Character;
using NexusForever.Server.Friendship.Game.Friend;
using NexusForever.Server.Friendship.Network.Internal;
using NexusForever.Server.Friendship.Network.Internal.Handler.Friendship;
using InternalIdentity = NexusForever.Network.Internal.Message.Shared.Identity;

namespace NexusForever.Server.Friendship.Tests
{
    public sealed class FriendshipRemoveIdentityHandlerTests
    {
        private const uint InviterAccountId = 10u;
        private const uint InviteeAccountId = 20u;
        private const ulong InviterCharacterId = 101ul;
        private const ulong InviteeCharacterId = 202ul;
        private const ulong FriendId = 42ul;
        private const ushort RealmId = 1;

        public static TheoryData<FriendshipType> ExactTypes => new()
        {
            FriendshipType.Friend,
            FriendshipType.Ignore,
            FriendshipType.Rival,
        };

        public static TheoryData<FriendshipType, FriendshipType> CompositeTransitions => new()
        {
            { FriendshipType.Friend, FriendshipType.Rival },
            { FriendshipType.Rival, FriendshipType.Friend },
        };

        public static IEnumerable<object[]> NoOpPairs
        {
            get
            {
                FriendshipType[] storedTypes =
                [
                    FriendshipType.Friend,
                    FriendshipType.Ignore,
                    FriendshipType.Rival,
                    FriendshipType.FriendAndRival,
                ];
                foreach (FriendshipType storedType in storedTypes)
                {
                    for (var rawType = 0; rawType < 16; rawType++)
                    {
                        var requestedType = (FriendshipType)rawType;
                        bool admitted = storedType == requestedType
                            && storedType is FriendshipType.Friend or FriendshipType.Ignore or FriendshipType.Rival;
                        admitted |= storedType == FriendshipType.FriendAndRival
                            && requestedType is FriendshipType.Friend or FriendshipType.Rival;
                        if (!admitted)
                            yield return [storedType, requestedType];
                    }
                }

                yield return [(FriendshipType)2, FriendshipType.Friend];
                yield return [FriendshipType.Account, FriendshipType.Friend];
            }
        }

        public static IEnumerable<object[]> RawRequestTypes => Enumerable.Range(0, 16)
            .Select(value => new object[] { (FriendshipType)value });

        [Theory]
        [MemberData(nameof(ExactTypes))]
        public async Task ExactPlainType_DeletesDirectedAggregateAndPublishesRemoved(FriendshipType type)
        {
            await using Fixture fixture = await Fixture.CreateAsync(type);

            int saveCalls = await fixture.HandleAsync(Message(type));

            Assert.Equal(1, saveCalls);
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            FriendshipContext context = scope.ServiceProvider.GetRequiredService<FriendshipContext>();
            Assert.Empty(await context.Friend.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.CharacterFriend.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.CharacterFriendInverse.AsNoTracking().ToArrayAsync());

            InternalMessageModel outbox = Assert.Single(await context.InternalMessage.AsNoTracking().ToArrayAsync());
            Assert.Equal(typeof(FriendshipRemovedMessage).AssemblyQualifiedName, outbox.Type);
            FriendshipRemovedMessage payload = JsonSerializer.Deserialize<FriendshipRemovedMessage>(outbox.Payload);
            Assert.NotNull(payload);
            Assert.Equal(FriendId, payload.Friend.Id);
            Assert.Equal(InviterCharacterId, payload.Friend.InviterCharacter.Identity.Id);
            Assert.Equal(InviteeCharacterId, payload.Friend.InviteeCharacter.Identity.Id);
            Assert.Equal(type, payload.Friend.Type);
        }

        [Theory]
        [MemberData(nameof(CompositeTransitions))]
        public async Task CompositeType_RemovesOnlyRequestedFacetAndPublishesUpdate(
            FriendshipType requestedType,
            FriendshipType expectedType)
        {
            await using Fixture fixture = await Fixture.CreateAsync(FriendshipType.FriendAndRival);

            int saveCalls = await fixture.HandleAsync(Message(requestedType));

            Assert.Equal(1, saveCalls);
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            FriendshipContext context = scope.ServiceProvider.GetRequiredService<FriendshipContext>();
            FriendModel friend = Assert.Single(await context.Friend.AsNoTracking().ToArrayAsync());
            Assert.Equal(expectedType, friend.Type);
            Assert.Single(await context.CharacterFriend.AsNoTracking().ToArrayAsync());
            Assert.Single(await context.CharacterFriendInverse.AsNoTracking().ToArrayAsync());

            InternalMessageModel outbox = Assert.Single(await context.InternalMessage.AsNoTracking().ToArrayAsync());
            Assert.Equal(typeof(FriendshipTypeUpdatedMessage).AssemblyQualifiedName, outbox.Type);
            FriendshipTypeUpdatedMessage payload = JsonSerializer.Deserialize<FriendshipTypeUpdatedMessage>(outbox.Payload);
            Assert.NotNull(payload);
            Assert.Equal(expectedType, payload.Friend.Type);
        }

        [Theory]
        [MemberData(nameof(NoOpPairs))]
        public async Task MismatchedOrUnsupportedType_IsNoOpBeforeSave(
            FriendshipType storedType,
            FriendshipType requestedType)
        {
            await using Fixture fixture = await Fixture.CreateAsync(storedType);

            int saveCalls = await fixture.HandleAsync(Message(requestedType));

            Assert.Equal(0, saveCalls);
            await AssertUnchangedAsync(fixture, storedType);
        }

        [Theory]
        [MemberData(nameof(CompositeTransitions))]
        public async Task DuplicateCompositeFacetRemoval_IsIdempotent(
            FriendshipType requestedType,
            FriendshipType expectedType)
        {
            await using Fixture fixture = await Fixture.CreateAsync(FriendshipType.FriendAndRival);

            int firstSaveCalls = await fixture.HandleAsync(Message(requestedType));
            int replaySaveCalls = await fixture.HandleAsync(Message(requestedType));

            Assert.Equal(1, firstSaveCalls);
            Assert.Equal(0, replaySaveCalls);
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            FriendshipContext context = scope.ServiceProvider.GetRequiredService<FriendshipContext>();
            Assert.Equal(expectedType, Assert.Single(await context.Friend.AsNoTracking().ToArrayAsync()).Type);
            Assert.Single(await context.InternalMessage.AsNoTracking().ToArrayAsync());
        }

        [Theory]
        [MemberData(nameof(CompositeTransitions))]
        public async Task OppositeCompositeFacetRemoval_DeletesRemainingAggregate(
            FriendshipType firstType,
            FriendshipType remainingType)
        {
            await using Fixture fixture = await Fixture.CreateAsync(FriendshipType.FriendAndRival);

            int firstSaveCalls = await fixture.HandleAsync(Message(firstType));
            int secondSaveCalls = await fixture.HandleAsync(Message(remainingType));

            Assert.Equal(1, firstSaveCalls);
            Assert.Equal(1, secondSaveCalls);
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            FriendshipContext context = scope.ServiceProvider.GetRequiredService<FriendshipContext>();
            Assert.Empty(await context.Friend.AsNoTracking().ToArrayAsync());
            InternalMessageModel[] outbox = await context.InternalMessage.AsNoTracking().ToArrayAsync();
            Assert.Equal(2, outbox.Length);
            Assert.Contains(outbox, message => message.Type == typeof(FriendshipTypeUpdatedMessage).AssemblyQualifiedName);
            Assert.Contains(outbox, message => message.Type == typeof(FriendshipRemovedMessage).AssemblyQualifiedName);
        }

        [Fact]
        public async Task WrongDirection_IsNoOp()
        {
            await using Fixture fixture = await Fixture.CreateAsync(FriendshipType.Friend);

            int saveCalls = await fixture.HandleAsync(Message(
                FriendshipType.Friend,
                InviteeCharacterId,
                InviterCharacterId));

            Assert.Equal(0, saveCalls);
            await AssertUnchangedAsync(fixture, FriendshipType.Friend);
        }

        [Theory]
        [MemberData(nameof(RawRequestTypes))]
        public async Task MissingAggregate_IsNoOp(FriendshipType requestedType)
        {
            await using Fixture fixture = await Fixture.CreateAsync(FriendshipType.Friend);

            int saveCalls = await fixture.HandleAsync(Message(
                requestedType,
                inviteeId: 999ul));

            Assert.Equal(0, saveCalls);
            await AssertUnchangedAsync(fixture, FriendshipType.Friend);
        }

        [Fact]
        public async Task MissingInviteeOnExactRemoval_PreservesAggregate()
        {
            await using Fixture fixture = await Fixture.CreateAsync(
                FriendshipType.Friend,
                includeInvitee: false);

            int saveCalls = await fixture.HandleAsync(Message(FriendshipType.Friend));

            Assert.Equal(0, saveCalls);
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            FriendshipContext context = scope.ServiceProvider.GetRequiredService<FriendshipContext>();
            Assert.Equal(FriendshipType.Friend, Assert.Single(await context.Friend.AsNoTracking().ToArrayAsync()).Type);
            Assert.Single(await context.CharacterFriend.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.InternalMessage.AsNoTracking().ToArrayAsync());
        }

        private static async Task AssertUnchangedAsync(Fixture fixture, FriendshipType expectedType)
        {
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            FriendshipContext context = scope.ServiceProvider.GetRequiredService<FriendshipContext>();
            Assert.Equal(expectedType, Assert.Single(await context.Friend.AsNoTracking().ToArrayAsync()).Type);
            Assert.Single(await context.CharacterFriend.AsNoTracking().ToArrayAsync());
            Assert.Single(await context.CharacterFriendInverse.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.InternalMessage.AsNoTracking().ToArrayAsync());
        }

        private static FriendshipRemoveIdentityMessage Message(
            FriendshipType type,
            ulong inviterId = InviterCharacterId,
            ulong inviteeId = InviteeCharacterId)
        {
            return new FriendshipRemoveIdentityMessage
            {
                Inviter = new InternalIdentity
                {
                    Id      = inviterId,
                    RealmId = RealmId,
                },
                Invitee = new InternalIdentity
                {
                    Id      = inviteeId,
                    RealmId = RealmId,
                },
                Type = type,
            };
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly SqliteConnection connection;
            private readonly HttpClient httpClient;

            public ServiceProvider Provider { get; }

            private Fixture(
                SqliteConnection connection,
                HttpClient httpClient,
                ServiceProvider provider)
            {
                this.connection = connection;
                this.httpClient = httpClient;
                Provider        = provider;
            }

            public static async Task<Fixture> CreateAsync(
                FriendshipType type,
                bool includeInvitee = true)
            {
                var connection = new SqliteConnection("Data Source=:memory:");
                await connection.OpenAsync();
                if (!includeInvitee)
                {
                    await using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = "PRAGMA foreign_keys = OFF;";
                    await command.ExecuteNonQueryAsync();
                }

                var httpClient = new HttpClient(new UnexpectedHttpMessageHandler())
                {
                    BaseAddress = new Uri("http://friendship-tests.invalid"),
                };
                DbContextOptions<FriendshipContext> options = new DbContextOptionsBuilder<FriendshipContext>()
                    .UseSqlite(connection)
                    .Options;
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddScoped(_ => new RecordingFriendshipContext(options));
                services.AddScoped<FriendshipContext>(serviceProvider =>
                    serviceProvider.GetRequiredService<RecordingFriendshipContext>());
                services.AddScoped<AccountRepository>();
                services.AddScoped<CharacterRepository>();
                services.AddScoped<FriendRepository>();
                services.AddScoped<InternalMessageRepository>();
                services.AddSingleton(new CharacterAPIClient(httpClient));
                services.AddSingleton(new AccountAPIClient(httpClient));
                services.AddTransient<OutboxMessagePublisher>();
                services.AddAccount();
                services.AddCharacter();
                services.AddFriend();
                services.AddTransient<FriendshipRemoveIdentityHandler>();

                ServiceProvider provider = services.BuildServiceProvider();
                var fixture = new Fixture(connection, httpClient, provider);
                await fixture.SeedAsync(type, includeInvitee);
                return fixture;
            }

            public async Task<int> HandleAsync(params FriendshipRemoveIdentityMessage[] messages)
            {
                await using AsyncServiceScope scope = Provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<RecordingFriendshipContext>();
                var handler = scope.ServiceProvider.GetRequiredService<FriendshipRemoveIdentityHandler>();
                foreach (FriendshipRemoveIdentityMessage message in messages)
                    await handler.Handle(message);
                return context.SaveCalls;
            }

            private async Task SeedAsync(FriendshipType type, bool includeInvitee)
            {
                await using AsyncServiceScope scope = Provider.CreateAsyncScope();
                FriendshipContext context = scope.ServiceProvider.GetRequiredService<FriendshipContext>();
                await context.Database.EnsureCreatedAsync();

                var inviterAccount = new AccountModel
                {
                    AccountId = InviterAccountId,
                    Email     = "inviter@example.invalid",
                    Nickname  = "InviterAccount",
                };
                var inviter = new CharacterModel
                {
                    CharacterId = InviterCharacterId,
                    RealmId     = RealmId,
                    RealmName   = "TestRealm",
                    Name        = "Inviter",
                    AccountId   = InviterAccountId,
                    Account     = inviterAccount,
                };
                context.Character.Add(inviter);

                if (includeInvitee)
                {
                    var inviteeAccount = new AccountModel
                    {
                        AccountId = InviteeAccountId,
                        Email     = "invitee@example.invalid",
                        Nickname  = "InviteeAccount",
                    };
                    context.Character.Add(new CharacterModel
                    {
                        CharacterId = InviteeCharacterId,
                        RealmId     = RealmId,
                        RealmName   = "TestRealm",
                        Name        = "Invitee",
                        AccountId   = InviteeAccountId,
                        Account     = inviteeAccount,
                    });
                }

                await context.SaveChangesAsync();

                context.Friend.Add(new FriendModel
                {
                    Id                 = FriendId,
                    InviterCharacterId = InviterCharacterId,
                    InviterRealmId     = RealmId,
                    InviteeCharacterId = InviteeCharacterId,
                    InviteeRealmId     = RealmId,
                    Note               = "test note",
                    Type               = type,
                });
                context.CharacterFriend.Add(new CharacterFriendModel
                {
                    CharacterId = InviterCharacterId,
                    RealmId     = RealmId,
                    FriendId    = FriendId,
                });
                if (includeInvitee)
                {
                    context.CharacterFriendInverse.Add(new CharacterFriendInverseModel
                    {
                        CharacterId = InviteeCharacterId,
                        RealmId     = RealmId,
                        FriendId    = FriendId,
                    });
                }
                await context.SaveChangesAsync();
            }

            public async ValueTask DisposeAsync()
            {
                await Provider.DisposeAsync();
                httpClient.Dispose();
                await connection.DisposeAsync();
            }
        }

        private sealed class RecordingFriendshipContext : FriendshipContext
        {
            public int SaveCalls { get; private set; }

            public RecordingFriendshipContext(DbContextOptions<FriendshipContext> options)
                : base(options)
            {
            }

            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                SaveCalls++;
                return base.SaveChangesAsync(cancellationToken);
            }
        }

        private sealed class UnexpectedHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("API fallback was not expected in friendship removal tests.");
            }
        }
    }
}
