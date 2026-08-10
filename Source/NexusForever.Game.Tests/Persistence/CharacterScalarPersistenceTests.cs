using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Reputation;
using NexusForever.Game.Entity;
using NexusForever.Game.Guild;
using NexusForever.Game.Map;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable.Model;
using NexusForever.Network.Internal;
using GameReputation = NexusForever.Game.Reputation.Reputation;
using PlayerPath = NexusForever.Game.Static.PlayerPath.Path;

namespace NexusForever.Game.Tests.Persistence
{
    public sealed class CharacterScalarPersistenceTests
    {
        [Fact]
        public void CurrencySave_FailedAttemptRemainsRetryableUntilAcknowledged()
        {
            var currency = new Currency(41ul, new CurrencyTypeEntry { Id = 1u }, 7ul);

            using TestCharacterContext failedContext = CreateContext();
            currency.Save(failedContext, new SaveCommitScope());

            Assert.Equal(EntityState.Added, Assert.Single(failedContext.ChangeTracker.Entries<CharacterCurrencyModel>()).State);

            using TestCharacterContext retryContext = CreateContext();
            var retryScope = new SaveCommitScope();
            currency.Save(retryContext, retryScope);

            Assert.Equal(EntityState.Added, Assert.Single(retryContext.ChangeTracker.Entries<CharacterCurrencyModel>()).State);
            retryScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext settledContext = CreateContext();
            currency.Save(settledContext, new SaveCommitScope());
            Assert.Empty(settledContext.ChangeTracker.Entries<CharacterCurrencyModel>());
        }

        [Fact]
        public void CurrencySave_PreservesMutationMadeWhileCreateCommitIsPending()
        {
            var currency = new Currency(43ul, new CurrencyTypeEntry { Id = 2u }, 5ul);
            using TestCharacterContext createContext = CreateContext();
            var createScope = new SaveCommitScope();
            currency.Save(createContext, createScope);

            currency.Amount = 19ul;
            createScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext updateContext = CreateContext();
            var updateScope = new SaveCommitScope();
            currency.Save(updateContext, updateScope);
            CharacterCurrencyModel update = Assert.Single(updateContext.ChangeTracker.Entries<CharacterCurrencyModel>()).Entity;

            Assert.Equal(19ul, update.Amount);
            Assert.True(updateContext.Entry(update).Property(model => model.Amount).IsModified);
            updateScope.CreateAcknowledgement().Acknowledge();
        }

        [Fact]
        public void PathSave_PreservesLatestValueAcrossPendingCommit()
        {
            var path = new PathEntry(new CharacterPathModel
            {
                Id      = 47ul,
                Path    = (byte)PlayerPath.Soldier,
                TotalXp = 3u
            });
            path.TotalXp = 8u;
            using TestCharacterContext firstContext = CreateContext();
            var firstScope = new SaveCommitScope();
            path.Save(firstContext, firstScope);

            path.TotalXp = 13u;
            firstScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext secondContext = CreateContext();
            var secondScope = new SaveCommitScope();
            path.Save(secondContext, secondScope);
            CharacterPathModel update = Assert.Single(secondContext.ChangeTracker.Entries<CharacterPathModel>()).Entity;

            Assert.Equal(13u, update.TotalXp);
            Assert.Equal(EntityState.Modified, secondContext.Entry(update).State);
            Assert.True(secondContext.Entry(update).Property(model => model.TotalXp).IsModified);
            secondScope.CreateAcknowledgement().Acknowledge();
        }

        [Fact]
        public void TitleSave_PreservesTimerChangeMadeDuringCreateCommit()
        {
            var title = new Title(53ul, new CharacterTitleEntry
            {
                Id              = 7u,
                LifeTimeSeconds = 60u
            });
            title.Revoked = true;
            using TestCharacterContext createContext = CreateContext();
            var createScope = new SaveCommitScope();
            title.Save(createContext, createScope);
            CharacterTitleModel create = Assert.Single(createContext.ChangeTracker.Entries<CharacterTitleModel>()).Entity;

            Assert.Equal((byte)1, create.Revoked);

            title.TimeRemaining = 25d;
            createScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext updateContext = CreateContext();
            var updateScope = new SaveCommitScope();
            title.Save(updateContext, updateScope);
            CharacterTitleModel update = Assert.Single(updateContext.ChangeTracker.Entries<CharacterTitleModel>()).Entity;

            Assert.Equal(25u, update.TimeRemaining);
            Assert.True(updateContext.Entry(update).Property(model => model.TimeRemaining).IsModified);
            updateScope.CreateAcknowledgement().Acknowledge();
        }

        [Fact]
        public void DatacubeSave_LoadedModelIsNotDirty()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(owner => owner.CharacterId).Returns(59ul);
            var datacube = new Datacube(player.Object, new CharacterDatacubeModel
            {
                Id       = 59ul,
                Type     = (byte)DatacubeType.Datacube,
                Datacube = 11,
                Progress = 4u
            });
            using TestCharacterContext context = CreateContext();

            datacube.Save(context, new SaveCommitScope());

            Assert.Empty(context.ChangeTracker.Entries<CharacterDatacubeModel>());
            Assert.Equal(4u, datacube.Progress);
        }

        [Fact]
        public void StatSave_CreatePersistsDataAndWaitsForAcknowledgement()
        {
            var stat = new StatValue(Stat.StandState, 3u, 17u);
            using TestCharacterContext firstContext = CreateContext();
            var firstScope = new SaveCommitScope();
            stat.SaveCharacter(61ul, firstContext, firstScope);
            CharacterStatModel create = Assert.Single(firstContext.ChangeTracker.Entries<CharacterStatModel>()).Entity;

            Assert.Equal(3f, create.Value);
            Assert.Equal(17u, create.Data);

            using TestCharacterContext retryContext = CreateContext();
            var retryScope = new SaveCommitScope();
            stat.SaveCharacter(61ul, retryContext, retryScope);
            Assert.Single(retryContext.ChangeTracker.Entries<CharacterStatModel>());
            retryScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext settledContext = CreateContext();
            stat.SaveCharacter(61ul, settledContext, new SaveCommitScope());
            Assert.Empty(settledContext.ChangeTracker.Entries<CharacterStatModel>());
        }

        [Fact]
        public void ReputationSave_PreservesMutationMadeDuringCreateCommit()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(owner => owner.CharacterId).Returns(67ul);
            var faction = new Mock<IFactionNode>();
            faction.SetupGet(node => node.FactionId).Returns(Faction.Exile);
            var reputation = new GameReputation(player.Object, faction.Object, 10f);
            using TestCharacterContext createContext = CreateContext();
            var createScope = new SaveCommitScope();
            reputation.Save(createContext, createScope);

            reputation.Amount = 15f;
            createScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext updateContext = CreateContext();
            var updateScope = new SaveCommitScope();
            reputation.Save(updateContext, updateScope);
            CharacterReputation update = Assert.Single(updateContext.ChangeTracker.Entries<CharacterReputation>()).Entity;

            Assert.Equal(15f, update.Amount);
            Assert.True(updateContext.Entry(update).Property(model => model.Amount).IsModified);
            updateScope.CreateAcknowledgement().Acknowledge();
        }

        [Fact]
        public void XpManager_LoadedValuesAreNotDirtyWithoutRestCalculation()
        {
            var player = new Mock<IPlayer>();
            var manager = new XpManager(player.Object, new CharacterModel
            {
                Id          = 71ul,
                TotalXp     = 120u,
                RestBonusXp = 30u,
                LastOnline  = null
            });
            using TestCharacterContext context = CreateContext();

            manager.Save(context, new SaveCommitScope());

            Assert.Empty(context.ChangeTracker.Entries<CharacterModel>());
            Assert.Equal(120u, manager.TotalXp);
            Assert.Equal(30u, manager.RestBonusXp);
        }

        [Fact]
        public void GuildManagerSave_PreservesAffiliationChangedDuringPendingCommit()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(owner => owner.CharacterId).Returns(73ul);
            var firstAffiliation = new Mock<IGuildBase>();
            firstAffiliation.SetupGet(guild => guild.Id).Returns(101ul);
            var secondAffiliation = new Mock<IGuildBase>();
            secondAffiliation.SetupGet(guild => guild.Id).Returns(102ul);
            var manager = new GuildManager(Mock.Of<IInternalMessagePublisher>());
            typeof(GuildManager)
                .GetField("owner", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, player.Object);
            manager.GuildAffiliation = firstAffiliation.Object;

            using TestCharacterContext firstContext = CreateContext();
            firstContext.Attach(new CharacterModel { Id = player.Object.CharacterId });
            var firstScope = new SaveCommitScope();
            manager.Save(firstContext, firstScope);

            manager.GuildAffiliation = secondAffiliation.Object;
            firstScope.CreateAcknowledgement().Acknowledge();

            using TestCharacterContext secondContext = CreateContext();
            CharacterModel character = secondContext.Attach(new CharacterModel { Id = player.Object.CharacterId }).Entity;
            var secondScope = new SaveCommitScope();
            manager.Save(secondContext, secondScope);

            Assert.Equal(102ul, character.GuildAffiliation);
            Assert.True(secondContext.Entry(character).Property(model => model.GuildAffiliation).IsModified);
            secondScope.CreateAcknowledgement().Acknowledge();
        }

        [Fact]
        public void ZoneMapDiscovery_RemainsRetryableUntilCommitIsAcknowledged()
        {
            var state = new ZoneMapDiscoverySaveState(true);
            int stageCount = 0;

            Assert.True(state.Stage(new SaveCommitScope(), () => stageCount++));

            var retryScope = new SaveCommitScope();
            Assert.True(state.Stage(retryScope, () => stageCount++));
            retryScope.CreateAcknowledgement().Acknowledge();

            Assert.False(state.Stage(new SaveCommitScope(), () => stageCount++));
            Assert.Equal(2, stageCount);
        }

        private static TestCharacterContext CreateContext()
        {
            return new TestCharacterContext();
        }

        private sealed class TestCharacterContext : CharacterContext
        {
            public TestCharacterContext()
                : base(new DbContextOptionsBuilder<CharacterContext>()
                    .UseMySql(
                        "Server=localhost;Database=nexus_forever_test;User=test;Password=test;",
                        new MySqlServerVersion(new Version(8, 0, 36)))
                    .Options)
            {
            }
        }
    }
}
