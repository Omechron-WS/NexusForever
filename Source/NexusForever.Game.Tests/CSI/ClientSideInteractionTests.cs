using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.CSI;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.CSI;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.CSI;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable.Model;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Collection;
using Moq;

namespace NexusForever.Game.Tests.CSI
{
    public class ClientSideInteractionTests
    {
        [Fact]
        public void Constructor_SetsProperties()
        {
            var mockPlayer = new Mock<IPlayer>();
            var mockEntity = new Mock<IWorldEntity>();

            var csi = new ClientSideInteraction(mockPlayer.Object, mockEntity.Object, 99u);

            Assert.Equal(99u, csi.ClientUniqueId);
            Assert.Same(mockEntity.Object, csi.ActivateUnit);
            Assert.Equal(CSIType.Interaction, csi.CsiType);
            Assert.Null(csi.Entry);
        }

        [Fact]
        public void Constructor_ThrowsOnNullOwner()
        {
            var mockEntity = new Mock<IWorldEntity>();
            Assert.Throws<ArgumentNullException>(() =>
                new ClientSideInteraction(null, mockEntity.Object, 1u));
        }

        [Fact]
        public void Constructor_ThrowsOnNullEntity()
        {
            var mockPlayer = new Mock<IPlayer>();
            Assert.Throws<ArgumentNullException>(() =>
                new ClientSideInteraction(mockPlayer.Object, null, 1u));
        }

        [Fact]
        public void TriggerSuccess_CallsEntityOnActivateSuccess()
        {
            (Mock<IPlayer> player, Mock<IWorldEntity> entity, Mock<IAssetManager> assetManager) = CreateValidInteraction();

            var csi = new ClientSideInteraction(player.Object, entity.Object, 42u, assetManager: assetManager.Object);

            Assert.True(csi.TriggerSuccess());
            Assert.False(csi.TriggerSuccess());

            entity.Verify(e => e.OnActivateSuccess(player.Object), Times.Once);
            player.VerifyGet(value => value.QuestManager, Times.AtLeastOnce);
            player.Object.QuestManager.VerifyObjective(QuestObjectiveType.ActivateEntity, 77u);
            player.Object.QuestManager.VerifyObjective(QuestObjectiveType.SucceedCSI, 77u);
            player.Object.QuestManager.VerifyObjective(QuestObjectiveType.ActivateTargetGroup, 12u);
            player.Object.QuestManager.VerifyObjective(QuestObjectiveType.ActivateTargetGroup, 13u);
        }

        [Fact]
        public void CompleteSuccess_ExcludesOnlyAuthoritativeActivateEntityObjective()
        {
            (Mock<IPlayer> player, Mock<IWorldEntity> entity, Mock<IAssetManager> assetManager) = CreateValidInteraction();
            var interaction = new ClientSideInteraction(
                player.Object,
                entity.Object,
                42u,
                assetManager: assetManager.Object);

            Assert.True(interaction.TrySuppressActivateEntityObjective(8_247u));
            Assert.False(interaction.TrySuppressActivateEntityObjective(8_247u));
            Assert.True(interaction.CompleteSuccess());

            Mock.Get(player.Object.QuestManager).Verify(manager => manager.ObjectiveUpdate(
                QuestObjectiveType.ActivateEntity,
                77u,
                1u,
                It.Is<IReadOnlySet<uint>>(excluded =>
                    excluded.Count == 1 && excluded.Contains(8_247u))), Times.Once);
            Mock.Get(player.Object.QuestManager).Verify(manager => manager.ObjectiveUpdate(
                QuestObjectiveType.ActivateEntity,
                77u,
                1u), Times.Never);
            player.Object.QuestManager.VerifyObjective(QuestObjectiveType.SucceedCSI, 77u);
            player.Object.QuestManager.VerifyObjective(QuestObjectiveType.ActivateTargetGroup, 12u);
            player.Object.QuestManager.VerifyObjective(QuestObjectiveType.ActivateTargetGroup, 13u);
            entity.Verify(value => value.OnActivateSuccess(player.Object), Times.Once);
        }

        [Fact]
        public void CompleteSuccess_RejectsLateSuppressionAndReplay()
        {
            (Mock<IPlayer> player, Mock<IWorldEntity> entity, Mock<IAssetManager> assetManager) = CreateValidInteraction();
            var interaction = new ClientSideInteraction(
                player.Object,
                entity.Object,
                42u,
                assetManager: assetManager.Object);

            Assert.True(interaction.CompleteSuccess());
            Assert.False(interaction.TrySuppressActivateEntityObjective(8_247u));
            Assert.False(interaction.CompleteSuccess());

            Mock.Get(player.Object.QuestManager).Verify(manager => manager.ObjectiveUpdate(
                QuestObjectiveType.ActivateEntity,
                77u,
                1u), Times.Once);
            Mock.Get(player.Object.QuestManager).Verify(manager => manager.ObjectiveUpdate(
                QuestObjectiveType.ActivateEntity,
                77u,
                1u,
                It.IsAny<IReadOnlySet<uint>>()), Times.Never);
            entity.Verify(value => value.OnActivateSuccess(player.Object), Times.Once);
        }

        [Fact]
        public void TriggerFail_CallsEntityOnActivateFail()
        {
            var mockPlayer = new Mock<IPlayer>();
            var mockEntity = new Mock<IWorldEntity>();

            var csi = new ClientSideInteraction(mockPlayer.Object, mockEntity.Object, 42u);
            Assert.True(csi.TriggerFail());
            Assert.False(csi.TriggerFail());

            mockEntity.Verify(e => e.OnActivateFail(mockPlayer.Object), Times.Once);
        }

        [Fact]
        public void TriggerSuccess_TargetLeavesWorld_TriggersFailureWithoutQuestProgress()
        {
            (Mock<IPlayer> player, Mock<IWorldEntity> entity, Mock<IAssetManager> assetManager) = CreateValidInteraction();
            entity.SetupGet(value => value.InWorld).Returns(false);
            var csi = new ClientSideInteraction(player.Object, entity.Object, 42u, assetManager: assetManager.Object);

            Assert.True(csi.TriggerSuccess());

            entity.Verify(value => value.OnActivateFail(player.Object), Times.Once);
            entity.Verify(value => value.OnActivateSuccess(It.IsAny<IPlayer>()), Times.Never);
            Mock.Get(player.Object.QuestManager).Verify(
                manager => manager.ObjectiveUpdate(It.IsAny<QuestObjectiveType>(), It.IsAny<uint>(), It.IsAny<uint>()),
                Times.Never);
        }

        [Fact]
        public void TriggerSuccess_TargetLeavesVisibility_TriggersFailure()
        {
            (Mock<IPlayer> player, Mock<IWorldEntity> entity, Mock<IAssetManager> assetManager) = CreateValidInteraction();
            player.Setup(value => value.GetVisible<IWorldEntity>(100u)).Returns((IWorldEntity)null);
            var csi = new ClientSideInteraction(player.Object, entity.Object, 42u, assetManager: assetManager.Object);

            Assert.True(csi.TriggerSuccess());

            entity.Verify(value => value.OnActivateFail(player.Object), Times.Once);
            entity.Verify(value => value.OnActivateSuccess(It.IsAny<IPlayer>()), Times.Never);
        }

        [Fact]
        public void Validator_RejectsDifferentMapAndInvalidRangeData()
        {
            (Mock<IPlayer> player, Mock<IWorldEntity> entity, _) = CreateValidInteraction();
            entity.SetupGet(value => value.Map).Returns(Mock.Of<IBaseMap>());

            Assert.False(ClientSideInteractionValidator.IsValid(player.Object, entity.Object));

            entity.SetupGet(value => value.Map).Returns(player.Object.Map);
            entity.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry
            {
                ActivateSpellMinRange = float.NaN,
                ActivateSpellMaxRange = 5f
            });
            Assert.False(ClientSideInteractionValidator.IsValid(player.Object, entity.Object));
        }

        [Theory]
        [InlineData(5f, true)]
        [InlineData(5.01f, false)]
        public void Validator_ZeroMaximumUsesBoundedServerFallback(float distance, bool expected)
        {
            (Mock<IPlayer> player, Mock<IWorldEntity> entity, _) = CreateValidInteraction();
            entity.SetupGet(value => value.Position).Returns(new Vector3(distance, 0f, 0f));
            entity.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry());

            Assert.Equal(expected, ClientSideInteractionValidator.IsValid(player.Object, entity.Object));
        }

        [Fact]
        public void ImplementsIClientSideInteraction()
        {
            Assert.True(typeof(IClientSideInteraction).IsAssignableFrom(typeof(ClientSideInteraction)));
        }

        [Fact]
        public void ISpellParameters_HasClientSideInteractionProperty()
        {
            var prop = typeof(ISpellParameters).GetProperty("ClientSideInteraction");
            Assert.NotNull(prop);
            Assert.Equal(typeof(IClientSideInteraction), prop.PropertyType);
        }

        [Fact]
        public void IWorldEntity_HasOnActivateSuccess()
        {
            var method = typeof(IWorldEntity).GetMethod("OnActivateSuccess");
            Assert.NotNull(method);
        }

        [Fact]
        public void IWorldEntity_HasOnActivateFail()
        {
            var method = typeof(IWorldEntity).GetMethod("OnActivateFail");
            Assert.NotNull(method);
        }

        [Fact]
        public void DefaultCsiTypeIsInteraction_WhenNoCsiEntry()
        {
            var mockPlayer = new Mock<IPlayer>();
            var mockEntity = new Mock<IWorldEntity>();

            var csi = new ClientSideInteraction(mockPlayer.Object, mockEntity.Object, 1u, 0);

            Assert.Equal(CSIType.Interaction, csi.CsiType);
        }

        [Fact]
        public void ResolveActivationSpell_SelectsFirstMetGuardBeforeFallback()
        {
            var entry = new Creature2Entry
            {
                Spell4IdActivate00              = 100u,
                PrerequisiteIdActivateSpell00   = 10u,
                Spell4IdActivate01              = 200u,
                PrerequisiteIdActivateSpell01   = 20u,
                Spell4IdActivate02              = 300u,
                PrerequisiteIdActivateSpell02   = 0u
            };

            uint spell4Id = WorldEntity.ResolveActivationSpell(entry, prerequisiteId => prerequisiteId == 20u);

            Assert.Equal(200u, spell4Id);
        }

        [Fact]
        public void ResolveActivationSpell_UsesLastContiguousUnguardedFallback()
        {
            var entry = new Creature2Entry
            {
                Spell4IdActivate00 = 100u,
                Spell4IdActivate01 = 200u,
                Spell4IdActivate03 = 400u
            };

            uint spell4Id = WorldEntity.ResolveActivationSpell(entry, _ => false);

            Assert.Equal(200u, spell4Id);
        }

        [Fact]
        public void ResolveActivationSpell_UnmetGuardedSlotsFailClosed()
        {
            var entry = new Creature2Entry
            {
                Spell4IdActivate00            = 100u,
                PrerequisiteIdActivateSpell00 = 10u,
                Spell4IdActivate01            = 200u,
                PrerequisiteIdActivateSpell01 = 20u
            };

            uint spell4Id = WorldEntity.ResolveActivationSpell(entry, _ => false);

            Assert.Equal(0u, spell4Id);
        }

        [Fact]
        public void CompleteSuccess_OrdersGenericThenEntityCoreThenScriptCallbacks()
        {
            var order = new List<string>();
            var questManager = new Mock<IQuestManager>();
            questManager.Setup(manager => manager.ObjectiveUpdate(
                    It.IsAny<QuestObjectiveType>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .Callback((QuestObjectiveType type, uint data, uint progress) => order.Add(type.ToString()));
            var datacubeManager = new Mock<IDatacubeManager>();
            datacubeManager.Setup(manager => manager.GetDatacube(11, DatacubeType.Datacube))
                .Returns((IDatacube)null);
            datacubeManager.Setup(manager => manager.AddDatacube(11, 0x04u))
                .Callback(() => order.Add("datacube"));
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);
            player.SetupGet(value => value.DatacubeManager).Returns(datacubeManager.Object);
            var scripts = new Mock<IScriptCollection>();
            scripts.Setup(collection => collection.Invoke<IWorldEntityScript>(
                    It.IsAny<Action<IWorldEntityScript>>()))
                .Callback(() => order.Add("script"));
            var entity = new TestSimpleEntity();
            typeof(WorldEntity).GetProperty(
                    nameof(WorldEntity.CreatureEntry),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(entity, new Creature2Entry
                {
                    Id         = 77u,
                    DatacubeId = 11u
                });
            typeof(SimpleEntity).GetProperty(
                    nameof(SimpleEntity.QuestChecklistIdx),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(entity, (byte)2);
            entity.SetScriptCollection(scripts.Object);
            var interaction = new ClientSideInteraction(player.Object, entity, 42u);

            Assert.True(interaction.CompleteSuccess());

            Assert.Equal(
                ["ActivateEntity", "SucceedCSI", "datacube", "ActivateTargetGroupChecklist", "script"],
                order);
        }

        private static (Mock<IPlayer> Player, Mock<IWorldEntity> Entity, Mock<IAssetManager> AssetManager) CreateValidInteraction()
        {
            var map = new Mock<IBaseMap>();
            var questManager = new Mock<IQuestManager>();
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.InWorld).Returns(true);
            player.SetupGet(value => value.Map).Returns(map.Object);
            player.SetupGet(value => value.Position).Returns(Vector3.Zero);
            player.SetupGet(value => value.QuestManager).Returns(questManager.Object);

            var entity = new Mock<IWorldEntity>();
            entity.SetupGet(value => value.InWorld).Returns(true);
            entity.SetupGet(value => value.Map).Returns(map.Object);
            entity.SetupGet(value => value.Position).Returns(new Vector3(3f, 0f, 0f));
            entity.SetupGet(value => value.CreatureId).Returns(77u);
            entity.SetupGet(value => value.Guid).Returns(100u);
            entity.SetupGet(value => value.CreatureEntry).Returns(new Creature2Entry
            {
                Id                    = 77u,
                ActivateSpellMinRange = 0f,
                ActivateSpellMaxRange = 5f
            });
            player.Setup(value => value.GetVisible<IWorldEntity>(100u)).Returns(entity.Object);

            var assetManager = new Mock<IAssetManager>();
            assetManager.Setup(manager => manager.GetTargetGroupsForCreatureId(77u))
                .Returns(ImmutableList.Create(12u, 13u));
            return (player, entity, assetManager);
        }

        private sealed class TestSimpleEntity : SimpleEntity
        {
            public TestSimpleEntity()
                : base(Mock.Of<NexusForever.Game.Abstract.Entity.Movement.IMovementManager>())
            {
            }

            public void SetScriptCollection(IScriptCollection value)
            {
                scriptCollection = value;
            }
        }
    }

    internal static class QuestManagerVerificationExtensions
    {
        public static void VerifyObjective(this IQuestManager questManager, QuestObjectiveType type, uint data)
        {
            Mock.Get(questManager).Verify(
                manager => manager.ObjectiveUpdate(type, data, 1u),
                Times.Once);
        }
    }
}
