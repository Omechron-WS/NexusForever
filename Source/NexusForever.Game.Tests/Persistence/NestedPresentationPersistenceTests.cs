using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Costume;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Tests.Persistence
{
    public class NestedPresentationPersistenceTests
    {
        [Fact]
        public void Appearance_FailedSaveRetainsDirtyState()
        {
            var appearance = new Appearance(new CharacterAppearanceModel
            {
                Id        = 1ul,
                Slot      = (byte)ItemSlot.ArmorHead,
                DisplayId = 2
            });
            appearance.DisplayId = 3;

            using (CharacterContext context = CreateContext())
                appearance.Save(context, new SaveCommitScope());

            using CharacterContext retryContext = CreateContext();
            appearance.Save(retryContext, new SaveCommitScope());

            CharacterAppearanceModel model = Assert.Single(retryContext.ChangeTracker.Entries<CharacterAppearanceModel>()).Entity;
            Assert.Equal(3, model.DisplayId);
        }

        [Fact]
        public void Bone_ConcurrentSameFieldMutationSurvivesAcknowledgement()
        {
            var bone = new Bone(new CharacterBoneModel
            {
                Id        = 1ul,
                BoneIndex = 2,
                Bone      = 1f
            });
            bone.BoneValue = 2f;
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                bone.Save(context, scope);
            bone.BoneValue = 3f;
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            bone.Save(retryContext, new SaveCommitScope());

            CharacterBoneModel model = Assert.Single(retryContext.ChangeTracker.Entries<CharacterBoneModel>()).Entity;
            Assert.Equal(3f, model.Bone);
        }

        [Fact]
        public void AppearanceManager_DeleteCancellationRetainsExactTombstoneAndQueuesCreate()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(1ul);
            var manager = new AppearanceManager(player.Object, new CharacterModel
            {
                Id = 1ul,
                Customisation =
                {
                    new CharacterCustomisationModel
                    {
                        Id    = 1ul,
                        Label = 2u,
                        Value = 3u
                    }
                }
            });
            ICustomisation customisation = Assert.Single(manager.GetCustomisations());
            customisation.Delete();
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                manager.Save(context, scope);
            customisation.EnqueueDelete(false);
            customisation.Value = 4u;

            Assert.Same(customisation, Assert.Single(manager.GetCustomisations()));
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            manager.Save(retryContext, new SaveCommitScope());

            CharacterCustomisationModel model = Assert.Single(retryContext.ChangeTracker.Entries<CharacterCustomisationModel>()).Entity;
            Assert.Equal(EntityState.Added, retryContext.Entry(model).State);
            Assert.Equal(4u, model.Value);
        }

        [Fact]
        public void AppearanceManager_FailedDeleteRetainsTombstoneUntilSuccessfulAcknowledgement()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(1ul);
            var manager = new AppearanceManager(player.Object, new CharacterModel
            {
                Id = 1ul,
                Customisation =
                {
                    new CharacterCustomisationModel
                    {
                        Id    = 1ul,
                        Label = 2u,
                        Value = 3u
                    }
                }
            });
            ICustomisation customisation = Assert.Single(manager.GetCustomisations());
            customisation.Delete();

            using (CharacterContext failedContext = CreateContext())
                manager.Save(failedContext, new SaveCommitScope());

            var retryScope = new SaveCommitScope();
            using (CharacterContext retryContext = CreateContext())
            {
                manager.Save(retryContext, retryScope);
                Assert.Equal(EntityState.Deleted, Assert.Single(retryContext.ChangeTracker.Entries<CharacterCustomisationModel>()).State);
            }

            retryScope.CreateAcknowledgement().Acknowledge();
            customisation.EnqueueDelete(false);
            Assert.Empty(manager.GetCustomisations());
        }

        [Fact]
        public void CostumeAndItem_ConcurrentMutationsSurviveAcknowledgement()
        {
            var costumeModel = new CharacterCostumeModel
            {
                Id             = 1ul,
                Index          = 2,
                VisibilityMask = 1u
            };
            costumeModel.CostumeItem.Add(new CharacterCostumeItemModel
            {
                Id      = 1ul,
                Index   = 2,
                Slot    = (byte)CostumeItemSlot.Head,
                Item2Id = 0u,
                DyeData = 10u
            });
            ICostume costume = new Costume(costumeModel);
            ICostumeItem item = costume.GetItem(CostumeItemSlot.Head);
            costume.VisibilityMask = 2u;
            item.DyeData = 20u;
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                costume.Save(context, scope);
            costume.VisibilityMask = 3u;
            item.DyeData = 30u;
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            costume.Save(retryContext, new SaveCommitScope());

            CharacterCostumeModel stagedCostume = Assert.Single(retryContext.ChangeTracker.Entries<CharacterCostumeModel>()).Entity;
            CharacterCostumeItemModel stagedItem = Assert.Single(retryContext.ChangeTracker.Entries<CharacterCostumeItemModel>()).Entity;
            Assert.Equal(3u, stagedCostume.VisibilityMask);
            Assert.Equal(30u, stagedItem.DyeData);
        }

        [Fact]
        public void CostumeItem_LoadedFieldsDoNotDirtySaveState()
        {
            var costume = new Mock<ICostume>();
            costume.SetupGet(value => value.Owner).Returns(1ul);
            costume.SetupGet(value => value.Index).Returns(2);
            var item = new CostumeItem(costume.Object, new CharacterCostumeItemModel
            {
                Id      = 1ul,
                Index   = 2,
                Slot    = (byte)CostumeItemSlot.Head,
                Item2Id = 0u,
                DyeData = 10u
            });

            using CharacterContext context = CreateContext();
            item.Save(context, new SaveCommitScope());

            Assert.Empty(context.ChangeTracker.Entries<CharacterCostumeItemModel>());
        }

        [Fact]
        public void CostumeManager_ConcurrentActiveIndexMutationSurvivesAcknowledgement()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(1ul);
            var manager = new CostumeManager(player.Object, new CharacterModel
            {
                Id                 = 1ul,
                ActiveCostumeIndex = -1
            });
            manager.CostumeIndex = 1;
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
            {
                context.Attach(new CharacterModel { Id = 1ul });
                manager.Save(context, scope);
            }

            manager.CostumeIndex = 2;
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            CharacterModel character = retryContext.Attach(new CharacterModel { Id = 1ul }).Entity;
            manager.Save(retryContext, new SaveCommitScope());

            Assert.Equal(2, character.ActiveCostumeIndex);
            Assert.True(retryContext.Entry(character).Property(model => model.ActiveCostumeIndex).IsModified);
        }

        [Fact]
        public void PetCustomisation_ConcurrentNameMutationDuringCreateSurvivesAcknowledgement()
        {
            IPetCustomisation customisation = new PetCustomisation(1ul, PetType.ScanBot, 2u)
            {
                Name = "First"
            };
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                customisation.Save(context, scope);
            customisation.Name = "Second";
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            customisation.Save(retryContext, new SaveCommitScope());

            CharacterPetCustomisationModel model = Assert.Single(retryContext.ChangeTracker.Entries<CharacterPetCustomisationModel>()).Entity;
            Assert.Equal("Second", model.Name);
        }

        [Fact]
        public void PetCustomisation_LoadedFieldsDoNotDirtySaveState()
        {
            IPetCustomisation customisation = new PetCustomisation(new CharacterPetCustomisationModel
            {
                Id          = 1ul,
                Type        = (byte)PetType.ScanBot,
                ObjectId    = 2u,
                Name        = "Loaded",
                FlairIdMask = 0ul
            });

            using CharacterContext context = CreateContext();
            customisation.Save(context, new SaveCommitScope());

            Assert.Empty(context.ChangeTracker.Entries<CharacterPetCustomisationModel>());
        }

        [Fact]
        public void PetFlair_SuccessfulAcknowledgementClearsCreate()
        {
            var flair = new PetFlair(1ul, new PetFlairEntry { Id = 2u });
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                flair.Save(context, scope);
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            flair.Save(retryContext, new SaveCommitScope());

            Assert.Empty(retryContext.ChangeTracker.Entries<CharacterPetFlairModel>());
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
