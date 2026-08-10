using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Housing;
using NexusForever.Game.Static.Housing;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.Housing
{
    public class HousingPersistenceTests
    {
        [Fact]
        public void Residence_SaveWithoutAcknowledgementRetainsDirtyState()
        {
            Residence residence = CreateResidence();
            residence.Name = "Updated";

            using (CharacterContext context = CreateContext())
                residence.Save(context, new SaveCommitScope());

            using CharacterContext retryContext = CreateContext();
            residence.Save(retryContext, new SaveCommitScope());

            ResidenceModel model = Assert.Single(retryContext.ChangeTracker.Entries<ResidenceModel>()).Entity;
            Assert.Equal("Updated", model.Name);
        }

        [Fact]
        public void Residence_ConcurrentSameFieldMutationSurvivesAcknowledgement()
        {
            Residence residence = CreateResidence();
            residence.Name = "First";
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                residence.Save(context, scope);
            residence.Name = "Second";
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            residence.Save(retryContext, new SaveCommitScope());

            ResidenceModel model = Assert.Single(retryContext.ChangeTracker.Entries<ResidenceModel>()).Entity;
            Assert.Equal("Second", model.Name);
        }

        [Fact]
        public void Decor_SuccessfulAcknowledgementClearsUnchangedDirtyState()
        {
            Decor decor = CreateDecor();
            decor.Scale = 2f;
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                decor.Save(context, scope, null);
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            decor.Save(retryContext, new SaveCommitScope(), null);

            Assert.Empty(retryContext.ChangeTracker.Entries<ResidenceDecor>());
        }

        [Fact]
        public void Decor_DeleteIsRetainedUntilAcknowledgement()
        {
            Decor decor = CreateDecor();
            decor.EnqueueDelete(true);
            bool removed = false;
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                decor.Save(context, scope, () => removed = true);

            Assert.True(decor.PendingDelete);
            Assert.False(removed);

            scope.CreateAcknowledgement().Acknowledge();

            Assert.False(decor.PendingDelete);
            Assert.True(removed);
        }

        [Fact]
        public void Decor_DeleteCancellationAfterStagingQueuesCompensatingCreate()
        {
            Decor decor = CreateDecor();
            decor.EnqueueDelete(true);
            bool removed = false;
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                decor.Save(context, scope, () => removed = true);
            decor.EnqueueDelete(false);
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            decor.Save(retryContext, new SaveCommitScope(), null);

            Assert.False(removed);
            Assert.True(decor.PendingCreate);
            Assert.Equal(EntityState.Added, Assert.Single(retryContext.ChangeTracker.Entries<ResidenceDecor>()).State);
        }

        [Fact]
        public void Plot_ConcurrentMutationDuringCreateSurvivesAcknowledgement()
        {
            var plot = new Plot(Mock.Of<IGameTableManager>());
            plot.Initialise(1ul, new HousingPlotInfoEntry
            {
                Id                       = 2u,
                HousingPropertyPlotIndex = 3u
            });
            var scope = new SaveCommitScope();

            using (CharacterContext context = CreateContext())
                plot.Save(context, scope);
            plot.BuildState = BuildState.Complete;
            scope.CreateAcknowledgement().Acknowledge();

            using CharacterContext retryContext = CreateContext();
            plot.Save(retryContext, new SaveCommitScope());

            ResidencePlotModel model = Assert.Single(retryContext.ChangeTracker.Entries<ResidencePlotModel>()).Entity;
            Assert.Equal(BuildState.Complete, model.BuildState);
        }

        private static Residence CreateResidence()
        {
            var realmContext = new Mock<IRealmContext>();
            realmContext.SetupGet(realm => realm.RealmId).Returns(1);
            var residence = new Residence(
                realmContext.Object,
                Mock.Of<IGameTableManager>(),
                Mock.Of<IGlobalResidenceManager>(),
                Mock.Of<IFactory<IPlot>>());
            residence.Initialise(new ResidenceModel
            {
                Id             = 1ul,
                OwnerId        = 2ul,
                PropertyInfoId = PropertyInfoId.Residence,
                Name           = "Original"
            });
            return residence;
        }

        private static Decor CreateDecor()
        {
            var residence = new Mock<IResidence>();
            residence.SetupGet(value => value.Identity).Returns(new Identity
            {
                RealmId = 1,
                Id      = 2ul
            });
            return new Decor(residence.Object, new ResidenceDecor
            {
                Id          = 2ul,
                DecorId     = 3ul,
                DecorInfoId = 4u,
                Scale       = 1f,
                Qw          = 1f
            }, new HousingDecorInfoEntry
            {
                Id = 4u
            });
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
