using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Tests.Persistence
{
    public class LoadoutPersistenceTests
    {
        [Fact]
        public void CharacterSpell_LoadedTierInitialisesMatchingSpellInfoWithoutDirtyingState()
        {
            Mock<ISpellBaseInfo> baseInfo = CreateSpellBaseInfo(3, out ISpellInfo tierThreeInfo);
            IPlayer player = CreatePlayer();
            ICharacterSpell spell = new CharacterSpell(player, new CharacterSpellModel
            {
                Id           = 1ul,
                Spell4BaseId = 2u,
                Tier         = 3
            }, baseInfo.Object, Mock.Of<IItem>());

            Assert.Equal(3, spell.Tier);
            Assert.Same(tierThreeInfo, spell.SpellInfo);
            baseInfo.Verify(info => info.GetSpellInfo(3), Times.Once);
            baseInfo.Verify(info => info.GetSpellInfo(0), Times.Never);

            using CharacterContext context = CreateContext();
            spell.Save(context, new SaveCommitScope());
            Assert.Empty(context.ChangeTracker.Entries<CharacterSpellModel>());
        }

        [Fact]
        public void CharacterSpell_TierMutationUsesNewTierAndSurvivesConcurrentMutation()
        {
            Mock<ISpellBaseInfo> baseInfo = CreateSpellBaseInfo(1, out _);
            ISpellInfo tierTwoInfo = CreateSpellInfo();
            ISpellInfo tierThreeInfo = CreateSpellInfo();
            baseInfo.Setup(info => info.GetSpellInfo(2)).Returns(tierTwoInfo);
            baseInfo.Setup(info => info.GetSpellInfo(3)).Returns(tierThreeInfo);
            ICharacterSpell spell = new CharacterSpell(CreatePlayer(), new CharacterSpellModel
            {
                Id           = 1ul,
                Spell4BaseId = 2u,
                Tier         = 1
            }, baseInfo.Object, Mock.Of<IItem>());
            spell.Tier = 2;
            Assert.Same(tierTwoInfo, spell.SpellInfo);
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                spell.Save(context, scope);
            spell.Tier = 3;
            Assert.Same(tierThreeInfo, spell.SpellInfo);
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            spell.Save(retryContext, new SaveCommitScope());

            CharacterSpellModel model = Assert.Single(retryContext.ChangeTracker.Entries<CharacterSpellModel>()).Entity;
            Assert.Equal(3, model.Tier);
        }

        [Fact]
        public void ActionSetShortcut_FailedSaveRetainsDirtyState()
        {
            ActionSet actionSet = CreateActionSet();
            var shortcut = new ActionSetShortcut(actionSet, new CharacterActionSetShortcutModel
            {
                Id           = 1ul,
                SpecIndex    = 0,
                Location     = 1,
                ShortcutType = (byte)ShortcutType.BagItem,
                ObjectId     = 2u,
                Tier         = 1
            });
            shortcut.ObjectId = 3u;

            using (CharacterContext context = CreateContext())
                shortcut.Save(context, new SaveCommitScope());

            using CharacterContext retryContext = CreateContext();
            shortcut.Save(retryContext, new SaveCommitScope());

            CharacterActionSetShortcutModel model = Assert.Single(retryContext.ChangeTracker.Entries<CharacterActionSetShortcutModel>()).Entity;
            Assert.Equal(3u, model.ObjectId);
        }

        [Fact]
        public void ActionSetShortcut_LoadedFieldsDoNotDirtySaveState()
        {
            var shortcut = new ActionSetShortcut(CreateActionSet(), new CharacterActionSetShortcutModel
            {
                Id           = 1ul,
                SpecIndex    = 0,
                Location     = 1,
                ShortcutType = (byte)ShortcutType.BagItem,
                ObjectId     = 2u,
                Tier         = 1
            });

            using CharacterContext context = CreateContext();
            shortcut.Save(context, new SaveCommitScope());

            Assert.Empty(context.ChangeTracker.Entries<CharacterActionSetShortcutModel>());
        }

        [Fact]
        public void ActionSet_DeleteCancellationRetainsExactShortcutAndQueuesCreate()
        {
            ActionSet actionSet = CreateActionSet();
            actionSet.AddShortcut(new CharacterActionSetShortcutModel
            {
                Id           = 1ul,
                SpecIndex    = 0,
                Location     = 1,
                ShortcutType = (byte)ShortcutType.BagItem,
                ObjectId     = 2u,
                Tier         = 1
            });
            IActionSetShortcut original = actionSet.GetShortcut((UILocation)1);
            actionSet.RemoveShortcut((UILocation)1);
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                actionSet.Save(context, scope);
            actionSet.AddShortcut((UILocation)1, ShortcutType.BagItem, 3u, 1);

            Assert.Same(original, actionSet.GetShortcut((UILocation)1));
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            actionSet.Save(retryContext, new SaveCommitScope());

            CharacterActionSetShortcutModel model = Assert.Single(retryContext.ChangeTracker.Entries<CharacterActionSetShortcutModel>()).Entity;
            Assert.Equal(EntityState.Added, retryContext.Entry(model).State);
            Assert.Equal(3u, model.ObjectId);
        }

        [Fact]
        public void ActionSet_SuccessfulDeleteRemovesTombstoneOnlyAfterAcknowledgement()
        {
            ActionSet actionSet = CreateActionSet();
            actionSet.AddShortcut(new CharacterActionSetShortcutModel
            {
                Id           = 1ul,
                SpecIndex    = 0,
                Location     = 1,
                ShortcutType = (byte)ShortcutType.BagItem,
                ObjectId     = 2u,
                Tier         = 1
            });
            IActionSetShortcut original = actionSet.GetShortcut((UILocation)1);
            actionSet.RemoveShortcut((UILocation)1);
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                actionSet.Save(context, scope);

            Assert.True(original.PendingDelete);

            scope.CreateAcknowledgement().Acknowledge();
            actionSet.AddShortcut((UILocation)1, ShortcutType.BagItem, 3u, 1);

            Assert.NotSame(original, actionSet.GetShortcut((UILocation)1));
        }

        [Fact]
        public void ActionSet_NewShortcutDeletionWaitsForAcknowledgementWithoutDatabaseWrite()
        {
            ActionSet actionSet = CreateActionSet();
            actionSet.AddShortcut((UILocation)1, ShortcutType.BagItem, 2u, 1);
            IActionSetShortcut original = actionSet.GetShortcut((UILocation)1);
            actionSet.RemoveShortcut((UILocation)1);
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
            {
                actionSet.Save(context, scope);
                Assert.Empty(context.ChangeTracker.Entries<CharacterActionSetShortcutModel>());
            }

            Assert.True(original.PendingCreate);
            Assert.True(original.PendingDelete);
            scope.CreateAcknowledgement().Acknowledge();
            actionSet.AddShortcut((UILocation)1, ShortcutType.BagItem, 3u, 1);

            Assert.NotSame(original, actionSet.GetShortcut((UILocation)1));
        }

        [Fact]
        public void ActionSetAmp_DeleteCancellationQueuesCompensatingCreate()
        {
            const uint ampId = 976u;
            ActionSet actionSet = CreateActionSet();
            var amp = new ActionSetAmp(actionSet, new EldanAugmentationEntry { Id = ampId }, false);
            amp.EnqueueDelete(true);
            bool removed = false;
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                amp.Save(context, scope, () => removed = true);
            amp.EnqueueDelete(false);
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            amp.Save(retryContext, new SaveCommitScope());

            Assert.False(removed);
            Assert.True(amp.PendingCreate);
            var entry = Assert.Single(retryContext.ChangeTracker.Entries<CharacterActionSetAmpModel>());
            Assert.Equal(EntityState.Added, entry.State);
            Assert.Equal((ushort)ampId, entry.Entity.AmpId);
        }

        [Fact]
        public void ActionSetAmp_CreateStagesExactUShortIdentity()
        {
            const uint ampId = 976u;
            var amp = new ActionSetAmp(
                CreateActionSet(),
                new EldanAugmentationEntry { Id = ampId },
                true);
            var scope = new SaveCommitScope();

            using CharacterContext context = CreateContext();
            amp.Save(context, scope);

            var entry = Assert.Single(
                context.ChangeTracker.Entries<CharacterActionSetAmpModel>());
            Assert.Equal(EntityState.Added, entry.State);
            Assert.Equal(1ul, entry.Entity.Id);
            Assert.Equal((byte)0, entry.Entity.SpecIndex);
            Assert.Equal((ushort)ampId, entry.Entity.AmpId);
        }

        [Fact]
        public void ActionSetAmp_DeleteStagesExactUShortIdentityAndAcknowledgesCallback()
        {
            const uint ampId = 976u;
            var amp = new ActionSetAmp(
                CreateActionSet(),
                new EldanAugmentationEntry { Id = ampId },
                false);
            amp.EnqueueDelete(true);
            bool removed = false;
            var scope = new SaveCommitScope();

            using CharacterContext context = CreateContext();
            amp.Save(context, scope, () => removed = true);

            var entry = Assert.Single(
                context.ChangeTracker.Entries<CharacterActionSetAmpModel>());
            Assert.Equal(EntityState.Deleted, entry.State);
            Assert.Equal(1ul, entry.Entity.Id);
            Assert.Equal((byte)0, entry.Entity.SpecIndex);
            Assert.Equal((ushort)ampId, entry.Entity.AmpId);
            Assert.False(removed);

            scope.CreateAcknowledgement().Acknowledge();

            Assert.True(removed);
        }

        [Fact]
        public void ActionSetAmp_OutOfRangeIdentityFailsBeforeTrackingOrAcknowledgement()
        {
            var amp = new ActionSetAmp(
                CreateActionSet(),
                new EldanAugmentationEntry { Id = (uint)ushort.MaxValue + 1u },
                true);
            var scope = new Mock<ISaveCommitScope>();

            using CharacterContext context = CreateContext();
            Assert.Throws<OverflowException>(() => amp.Save(context, scope.Object));

            Assert.Empty(context.ChangeTracker.Entries<CharacterActionSetAmpModel>());
            scope.Verify(value => value.Register(It.IsAny<Action>()), Times.Never);
            Assert.True(amp.PendingCreate);
        }

        private static ActionSet CreateActionSet()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(1ul);
            return new ActionSet(0, player.Object);
        }

        private static IPlayer CreatePlayer()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(1ul);
            return player.Object;
        }

        private static Mock<ISpellBaseInfo> CreateSpellBaseInfo(byte tier, out ISpellInfo spellInfo)
        {
            spellInfo = CreateSpellInfo();
            var baseInfo = new Mock<ISpellBaseInfo>();
            baseInfo.SetupGet(value => value.Entry).Returns(new Spell4BaseEntry { Id = 2u });
            baseInfo.Setup(value => value.GetSpellInfo(tier)).Returns(spellInfo);
            return baseInfo;
        }

        private static ISpellInfo CreateSpellInfo()
        {
            var spellInfo = new Mock<ISpellInfo>();
            spellInfo.SetupGet(value => value.Entry).Returns(new Spell4Entry
            {
                AbilityChargeCount = 0u
            });
            return spellInfo.Object;
        }

        private static CharacterContext CreateContext()
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
