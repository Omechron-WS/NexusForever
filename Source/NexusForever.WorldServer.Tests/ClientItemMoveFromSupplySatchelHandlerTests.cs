using System.Collections.Generic;
using System.Reflection;
using Moq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;
using NexusForever.WorldServer.Network.Message.Handler.Item;

namespace NexusForever.WorldServer.Tests
{
    public sealed class ClientItemMoveFromSupplySatchelHandlerTests
    {
        private const ulong CharacterId = 42ul;
        private const ushort MaterialId = 126;
        private const uint ItemId = 84u;
        private const uint StackCount = 5u;

        [Fact]
        public void ZeroAmount_IsIgnoredBeforePlayerAccess()
        {
            var session = new Mock<IWorldSession>();
            var handler = new ClientItemMoveFromSupplySatchelHandler();

            handler.HandleMessage(session.Object, CreateMessage(MaterialId, 0u));

            session.VerifyGet(value => value.Player, Times.Never);
        }

        [Theory]
        [InlineData(1u)]
        [InlineData(3u)]
        [InlineData(StackCount)]
        public void PositiveBoundedAmount_IsForwardedUnchanged(uint amount)
        {
            SatchelFixture fixture = CreateFixture();

            fixture.Handler.HandleMessage(fixture.Session.Object, CreateMessage(MaterialId, amount));

            fixture.Manager.Verify(
                value => value.MoveToInventory(MaterialId, amount),
                Times.Once);
        }

        [Theory]
        [InlineData(SourceFailure.MissingMaterial)]
        [InlineData(SourceFailure.MissingOwner)]
        [InlineData(SourceFailure.WrongOwner)]
        [InlineData(SourceFailure.ZeroStack)]
        [InlineData(SourceFailure.OversizedAmount)]
        [InlineData(SourceFailure.MaximumAmount)]
        [InlineData(SourceFailure.MissingEntry)]
        [InlineData(SourceFailure.MismatchedEntry)]
        [InlineData(SourceFailure.MissingItemMapping)]
        public void InvalidLiveMaterial_IsIgnoredBeforeWithdrawal(SourceFailure failure)
        {
            SatchelFixture fixture = CreateFixture();
            uint requestAmount = 1u;
            switch (failure)
            {
                case SourceFailure.MissingMaterial:
                    SetMaterials(fixture.Manager);
                    break;
                case SourceFailure.MissingOwner:
                    fixture.Material.SetupGet(value => value.Owner).Returns(0ul);
                    break;
                case SourceFailure.WrongOwner:
                    fixture.Material.SetupGet(value => value.Owner).Returns(CharacterId + 1ul);
                    break;
                case SourceFailure.ZeroStack:
                    fixture.Material.SetupGet(value => value.Amount).Returns((ushort)0);
                    break;
                case SourceFailure.OversizedAmount:
                    requestAmount = StackCount + 1u;
                    break;
                case SourceFailure.MaximumAmount:
                    requestAmount = uint.MaxValue;
                    break;
                case SourceFailure.MissingEntry:
                    fixture.Material.SetupGet(value => value.Entry).Returns((TradeskillMaterialEntry)null);
                    break;
                case SourceFailure.MismatchedEntry:
                    fixture.Material.SetupGet(value => value.Entry).Returns(new TradeskillMaterialEntry
                    {
                        Id = MaterialId + 1u,
                        Item2IdStatRevolution = ItemId
                    });
                    break;
                case SourceFailure.MissingItemMapping:
                    fixture.Material.SetupGet(value => value.Entry).Returns(new TradeskillMaterialEntry
                    {
                        Id = MaterialId,
                        Item2IdStatRevolution = 0u
                    });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }

            fixture.Handler.HandleMessage(
                fixture.Session.Object,
                CreateMessage(MaterialId, requestAmount));

            fixture.Manager.Verify(
                value => value.MoveToInventory(It.IsAny<ushort>(), It.IsAny<uint>()),
                Times.Never);
        }

        private static SatchelFixture CreateFixture()
        {
            var material = new Mock<ITradeskillMaterial>();
            material.SetupGet(value => value.Owner).Returns(CharacterId);
            material.SetupGet(value => value.MaterialId).Returns(MaterialId);
            material.SetupGet(value => value.Amount).Returns((ushort)StackCount);
            material.SetupGet(value => value.Entry).Returns(new TradeskillMaterialEntry
            {
                Id = MaterialId,
                Item2IdStatRevolution = ItemId
            });

            var manager = new Mock<ISupplySatchelManager>();
            SetMaterials(manager, material.Object);

            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.CharacterId).Returns(CharacterId);
            player.SetupGet(value => value.SupplySatchelManager).Returns(manager.Object);

            var session = new Mock<IWorldSession>();
            session.SetupGet(value => value.Player).Returns(player.Object);

            return new SatchelFixture(
                new ClientItemMoveFromSupplySatchelHandler(),
                session,
                manager,
                material);
        }

        private static void SetMaterials(
            Mock<ISupplySatchelManager> manager,
            params ITradeskillMaterial[] materials)
        {
            manager
                .Setup(value => value.GetEnumerator())
                .Returns(() => ((IEnumerable<ITradeskillMaterial>)materials).GetEnumerator());
        }

        private static ClientItemMoveFromSupplySatchel CreateMessage(ushort materialId, uint amount)
        {
            var message = new ClientItemMoveFromSupplySatchel();
            typeof(ClientItemMoveFromSupplySatchel)
                .GetProperty(
                    nameof(ClientItemMoveFromSupplySatchel.MaterialId),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, materialId);
            typeof(ClientItemMoveFromSupplySatchel)
                .GetProperty(
                    nameof(ClientItemMoveFromSupplySatchel.Amount),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(message, amount);
            return message;
        }

        public enum SourceFailure
        {
            MissingMaterial,
            MissingOwner,
            WrongOwner,
            ZeroStack,
            OversizedAmount,
            MaximumAmount,
            MissingEntry,
            MismatchedEntry,
            MissingItemMapping
        }

        private sealed record SatchelFixture(
            ClientItemMoveFromSupplySatchelHandler Handler,
            Mock<IWorldSession> Session,
            Mock<ISupplySatchelManager> Manager,
            Mock<ITradeskillMaterial> Material);
    }
}
