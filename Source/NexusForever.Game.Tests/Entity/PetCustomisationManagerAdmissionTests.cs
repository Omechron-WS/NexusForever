using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Network;
using NexusForever.Shared;

namespace NexusForever.Game.Tests.Entity
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class PetCustomisationManagerAdmissionCollection
    {
        public const string Name = "Pet customisation manager admission";
    }

    [Collection(PetCustomisationManagerAdmissionCollection.Name)]
    public sealed class PetCustomisationManagerAdmissionTests : IDisposable
    {
        public enum PetOperation
        {
            Rename,
            AddCustomisation
        }

        private const uint PetObjectId = 123u;

        private readonly IServiceProvider previousProvider;
        private readonly Mock<IServiceProvider> provider = new(MockBehavior.Strict);

        public PetCustomisationManagerAdmissionTests()
        {
            previousProvider = LegacyServiceProvider.Provider;
            LegacyServiceProvider.Provider = provider.Object;
        }

        public void Dispose()
        {
            LegacyServiceProvider.Provider = previousProvider;
        }

        public static TheoryData<PetOperation, PetType> InvalidPetTypes => new()
        {
            { PetOperation.Rename, (PetType)3 },
            { PetOperation.Rename, (PetType)256 },
            { PetOperation.AddCustomisation, (PetType)3 },
            { PetOperation.AddCustomisation, (PetType)256 }
        };

        [Theory]
        [MemberData(nameof(InvalidPetTypes))]
        public void InvalidPetType_RejectsBeforeDependenciesOrCustomisationMutation(
            PetOperation operation,
            PetType type)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var model = new CharacterModel();
            var manager = new PetCustomisationManager(player.Object, model);

            Exception exception = Record.Exception(() => Invoke(operation, manager, type));

            Assert.IsType<InvalidPacketValueException>(exception);
            VerifyNoCustomisation(manager, model, player, type);
        }

        [Theory]
        [InlineData(PetType.ScanBot)]
        [InlineData(PetType.GroundMount)]
        [InlineData(PetType.HoverBoard)]
        public void ValidPetType_InvalidIndexRetainsExistingArgumentOutOfRange(PetType type)
        {
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var model = new CharacterModel();
            var manager = new PetCustomisationManager(player.Object, model);

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.AddCustomisation(
                type,
                PetObjectId,
                PetCustomisationManager.MaxCustomisationFlairs,
                0));

            VerifyNoCustomisation(manager, model, player, type);
        }

        private static void Invoke(PetOperation operation, PetCustomisationManager manager, PetType type)
        {
            switch (operation)
            {
                case PetOperation.Rename:
                    manager.RenamePet(type, PetObjectId, "Scanbot");
                    break;
                case PetOperation.AddCustomisation:
                    manager.AddCustomisation(type, PetObjectId, ushort.MaxValue, ushort.MaxValue);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        private void VerifyNoCustomisation(
            PetCustomisationManager manager,
            CharacterModel model,
            Mock<IPlayer> player,
            PetType type)
        {
            Assert.Null(manager.GetCustomisation(type, PetObjectId));
            Assert.Empty(model.PetCustomisation);

            using var context = new TestCharacterContext();
            manager.Save(context);
            Assert.Empty(context.ChangeTracker.Entries<CharacterPetCustomisationModel>());

            provider.Verify(serviceProvider => serviceProvider.GetService(It.IsAny<Type>()), Times.Never);
            player.VerifyNoOtherCalls();
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
