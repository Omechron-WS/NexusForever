using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map.Lock;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Configuration.Model;
using NexusForever.Game.Map.Instance;
using NexusForever.Game.Static.Housing;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.World.Message.Model.Housing;
using NexusForever.Network.World.Message.Static;
using NexusForever.Script;
using NexusForever.Shared;
using NexusForever.Shared.Configuration;

namespace NexusForever.Game.Tests.Housing
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class ResidenceMapModificationCollection
    {
        public const string Name = "Residence map modification";
    }

    [Collection(ResidenceMapModificationCollection.Name)]
    public sealed class ResidenceMapModificationTests
    {
        private const ushort RealmId = 1;
        private const ulong ResidenceId = 10ul;

        [Fact]
        public void CrateAllDecor_RequiresDecorateAuthorityBeforeReadingDecor()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence();
            residence
                .Setup(value => value.CanModifyResidence(
                    fixture.Player.Object,
                    ResidenceModification.Decorate))
                .Returns(false);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.CrateAllDecor(
                CreateIdentity(ResidenceId),
                fixture.Player.Object));

            residence.Verify(value => value.GetPlacedDecor(), Times.Never);
        }

        [Fact]
        public void DecorCreate_LateInvalidEntryDoesNotMutateEarlierEntry()
        {
            var entry = new HousingDecorInfoEntry { Id = 100u, MinScale = 0.2f, MaxScale = 2f };
            var fixture = new MapFixture(decorEntries: [entry]);
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.Decorate);
            residence
                .Setup(value => value.DecorCreate(entry))
                .Returns(CreateDecor(1ul, DecorType.Crate).Object);
            ClientHousingDecorUpdate message = CreateDecorUpdate(
                DecorUpdateOperation.Create,
                CreateDecorInfo(ResidenceId, decorInfoId: entry.Id),
                CreateDecorInfo(ResidenceId, decorInfoId: 999u));

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.DecorUpdate(
                fixture.Player.Object,
                message));

            residence.Verify(value => value.DecorCreate(It.IsAny<HousingDecorInfoEntry>()), Times.Never);
        }

        [Fact]
        public void DecorChange_LateInvalidColourDoesNotMoveEarlierDecor()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.Decorate);
            Mock<IDecor> firstDecor = CreateDecor(1ul, DecorType.FreePlace);
            Mock<IDecor> secondDecor = CreateDecor(2ul, DecorType.FreePlace);
            residence.Setup(value => value.GetDecor(1ul)).Returns(firstDecor.Object);
            residence.Setup(value => value.GetDecor(2ul)).Returns(secondDecor.Object);
            ClientHousingDecorUpdate message = CreateDecorUpdate(
                DecorUpdateOperation.Change,
                CreateDecorInfo(ResidenceId, decorId: 1ul),
                CreateDecorInfo(ResidenceId, decorId: 2ul, colourShiftId: 99));

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.DecorUpdate(
                fixture.Player.Object,
                message));

            firstDecor.Verify(value => value.Move(
                It.IsAny<DecorType>(),
                It.IsAny<Vector3>(),
                It.IsAny<Quaternion>(),
                It.IsAny<float>(),
                It.IsAny<uint>()), Times.Never);
            firstDecor.Verify(value => value.Crate(), Times.Never);
        }

        [Theory]
        [InlineData(0.1f)]
        [InlineData(2.1f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void DecorCreate_ScaleOutsideAuthoritativeRangeDoesNotCreate(float scale)
        {
            var entry = new HousingDecorInfoEntry { Id = 100u, MinScale = 0.2f, MaxScale = 2f };
            var fixture = new MapFixture(decorEntries: [entry]);
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.Decorate);
            ClientHousingDecorUpdate message = CreateDecorUpdate(
                DecorUpdateOperation.Create,
                CreateDecorInfo(ResidenceId, decorInfoId: entry.Id, scale: scale));

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.DecorUpdate(
                fixture.Player.Object,
                message));

            residence.Verify(value => value.DecorCreate(It.IsAny<HousingDecorInfoEntry>()), Times.Never);
        }

        [Fact]
        public void DecorChange_UsesStoredEntryScaleRange()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.Decorate);
            Mock<IDecor> decor = CreateDecor(
                1ul,
                DecorType.FreePlace,
                new HousingDecorInfoEntry { MinScale = 0.5f, MaxScale = 2f });
            residence.Setup(value => value.GetDecor(1ul)).Returns(decor.Object);
            ClientHousingDecorUpdate message = CreateDecorUpdate(
                DecorUpdateOperation.Change,
                CreateDecorInfo(ResidenceId, decorId: 1ul, scale: 2.1f));

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.DecorUpdate(
                fixture.Player.Object,
                message));

            decor.Verify(value => value.Move(
                It.IsAny<DecorType>(),
                It.IsAny<Vector3>(),
                It.IsAny<Quaternion>(),
                It.IsAny<float>(),
                It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void DecorChange_DuplicateTargetIsRejectedBeforeMutation()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.Decorate);
            Mock<IDecor> decor = CreateDecor(1ul, DecorType.FreePlace);
            residence.Setup(value => value.GetDecor(1ul)).Returns(decor.Object);
            ClientHousingDecorUpdate message = CreateDecorUpdate(
                DecorUpdateOperation.Change,
                CreateDecorInfo(ResidenceId, decorId: 1ul),
                CreateDecorInfo(ResidenceId, decorId: 1ul));

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.DecorUpdate(
                fixture.Player.Object,
                message));

            decor.Verify(value => value.Move(
                It.IsAny<DecorType>(),
                It.IsAny<Vector3>(),
                It.IsAny<Quaternion>(),
                It.IsAny<float>(),
                It.IsAny<uint>()), Times.Never);
        }

        [Fact]
        public void DecorCreate_CrateCanonicalisesStoredState()
        {
            var entry = new HousingDecorInfoEntry { Id = 100u, MinScale = 0.2f, MaxScale = 2f };
            var fixture = new MapFixture(decorEntries: [entry]);
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.Decorate);
            Mock<IDecor> decor = CreateDecor(1ul, DecorType.Crate, entry);
            residence.Setup(value => value.DecorCreate(entry)).Returns(decor.Object);
            ClientHousingDecorUpdate message = CreateDecorUpdate(
                DecorUpdateOperation.Create,
                CreateDecorInfo(
                    ResidenceId,
                    decorInfoId: entry.Id,
                    decorType: DecorType.Crate,
                    plotIndex: 17u,
                    scale: float.NaN));

            fixture.Map.DecorUpdate(fixture.Player.Object, message);

            decor.Verify(value => value.Crate(), Times.Once);
            decor.VerifySet(value => value.PlotIndex = It.IsAny<uint>(), Times.Never);
        }

        [Fact]
        public void DecorDelete_LatePlacedDecorDoesNotDeleteEarlierCratedDecor()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.DeleteDecor);
            Mock<IDecor> cratedDecor = CreateDecor(1ul, DecorType.Crate);
            Mock<IDecor> placedDecor = CreateDecor(2ul, DecorType.FreePlace);
            residence.Setup(value => value.GetDecor(1ul)).Returns(cratedDecor.Object);
            residence.Setup(value => value.GetDecor(2ul)).Returns(placedDecor.Object);
            ClientHousingDecorUpdate message = CreateDecorUpdate(
                DecorUpdateOperation.Delete,
                CreateDecorInfo(ResidenceId, decorId: 1ul, decorType: DecorType.FreePlace),
                CreateDecorInfo(ResidenceId, decorId: 2ul, decorType: DecorType.Crate));

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.DecorUpdate(
                fixture.Player.Object,
                message));

            cratedDecor.Verify(value => value.EnqueueDelete(It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public void DecorDelete_UsesStoredPlacedTypeInsteadOfPacketCrateClaims()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.DeleteDecor);
            Mock<IDecor> decor = CreateDecor(1ul, DecorType.FreePlace);
            decor.SetupProperty(value => value.Position, Vector3.Zero);
            residence.Setup(value => value.GetDecor(1ul)).Returns(decor.Object);
            ClientHousingDecorUpdate message = CreateDecorUpdate(
                DecorUpdateOperation.Delete,
                CreateDecorInfo(
                    ResidenceId,
                    decorId: 1ul,
                    decorType: DecorType.Crate,
                    position: Vector3.Zero));

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.DecorUpdate(
                fixture.Player.Object,
                message));

            decor.Verify(value => value.EnqueueDelete(It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public void DecorDelete_UsesStoredCrateTypeInsteadOfPacketPlacementClaims()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.DeleteDecor);
            Mock<IDecor> decor = CreateDecor(1ul, DecorType.Crate);
            residence.Setup(value => value.GetDecor(1ul)).Returns(decor.Object);
            ClientHousingDecorUpdate message = CreateDecorUpdate(
                DecorUpdateOperation.Delete,
                CreateDecorInfo(
                    ResidenceId,
                    decorId: 1ul,
                    decorType: DecorType.FreePlace,
                    position: new Vector3(5f, 6f, 7f)));

            fixture.Map.DecorUpdate(fixture.Player.Object, message);

            decor.Verify(value => value.EnqueueDelete(true), Times.Once);
            residence.Verify(value => value.CanModifyResidence(
                fixture.Player.Object,
                ResidenceModification.DeleteDecor), Times.Once);
        }

        [Fact]
        public void RenameResidence_UsesRenameAuthorityBeforeMutation()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence();
            residence.SetupProperty(value => value.Name, "Before");
            residence
                .Setup(value => value.CanModifyResidence(
                    fixture.Player.Object,
                    ResidenceModification.Rename))
                .Returns(false);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.RenameResidence(
                fixture.Player.Object,
                CreateIdentity(ResidenceId),
                "After"));

            Assert.Equal("Before", residence.Object.Name);
        }

        [Fact]
        public void Remodel_LateInvalidFieldDoesNotMutateEarlierField()
        {
            var wallpaper = new HousingWallpaperInfoEntry { Id = 20u, Flags = 0x2u };
            var fixture = new MapFixture(wallpaperEntries: [wallpaper]);
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.Remodel);
            residence.SetupProperty(value => value.Wallpaper, (ushort)1);
            residence.SetupProperty(value => value.Door, (ushort)2);
            ClientHousingRemodel message = CreateRemodel(
                ResidenceId,
                wallpaperId: wallpaper.Id,
                doorDecorInfoId: 999u);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.Remodel(
                CreateIdentity(ResidenceId),
                fixture.Player.Object,
                message));

            Assert.Equal((ushort)1, residence.Object.Wallpaper);
            Assert.Equal((ushort)2, residence.Object.Door);
        }

        [Fact]
        public void Remodel_RejectsExistingWallpaperOfWrongSubtypeBeforeMutation()
        {
            var sky = new HousingWallpaperInfoEntry { Id = 20u, Flags = 0x40u };
            var fixture = new MapFixture(wallpaperEntries: [sky]);
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.Remodel);
            residence.SetupProperty(value => value.Wallpaper, (ushort)1);
            ClientHousingRemodel message = CreateRemodel(
                ResidenceId,
                wallpaperId: sky.Id);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.Remodel(
                CreateIdentity(ResidenceId),
                fixture.Player.Object,
                message));

            Assert.Equal((ushort)1, residence.Object.Wallpaper);
        }

        [Fact]
        public void Remodel_RejectsExistingDecorOfWrongSubtypeBeforeMutation()
        {
            var furnishing = new HousingDecorInfoEntry
            {
                Id = 10u,
                HousingDecorTypeId = 9u
            };
            var fixture = new MapFixture(decorEntries: [furnishing]);
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.Remodel);
            residence.SetupProperty(value => value.Roof, (ushort)1);
            ClientHousingRemodel message = CreateRemodel(
                ResidenceId,
                roofDecorInfoId: furnishing.Id);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.Remodel(
                CreateIdentity(ResidenceId),
                fixture.Player.Object,
                message));

            Assert.Equal((ushort)1, residence.Object.Roof);
        }

        [Fact]
        public void CommunityRemodel_RejectsHouseExteriorFieldsBeforeSharedMutation()
        {
            var roof = new HousingDecorInfoEntry { Id = 10u };
            var sky = new HousingWallpaperInfoEntry { Id = 21u, Flags = 0x40u };
            var fixture = new MapFixture(decorEntries: [roof], wallpaperEntries: [sky]);
            Mock<IResidence> residence = fixture.AddResidence(ResidenceType.Community);
            Allow(residence, fixture.Player, ResidenceModification.Remodel);
            residence.SetupProperty(value => value.Roof, (ushort)1);
            residence.SetupProperty(value => value.Sky, (ushort)2);
            ClientHousingRemodel message = CreateRemodel(
                ResidenceId,
                roofDecorInfoId: roof.Id,
                skyWallpaperId: sky.Id);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.Remodel(
                CreateIdentity(ResidenceId),
                fixture.Player.Object,
                message));

            Assert.Equal((ushort)1, residence.Object.Roof);
            Assert.Equal((ushort)2, residence.Object.Sky);
        }

        [Fact]
        public void CommunityRemodel_AllowsValidatedSharedOptions()
        {
            var sky = new HousingWallpaperInfoEntry { Id = 21u, Flags = 0x40u };
            var music = new HousingWallpaperInfoEntry { Id = 22u, Flags = 0x100u };
            var ground = new HousingWallpaperInfoEntry { Id = 23u, Flags = 0x200u };
            var fixture = new MapFixture(wallpaperEntries: [sky, music, ground]);
            Mock<IResidence> residence = fixture.AddResidence(ResidenceType.Community);
            Allow(residence, fixture.Player, ResidenceModification.Remodel);
            residence.SetupProperty(value => value.Sky, (ushort)1);
            residence.SetupProperty(value => value.Music, (ushort)2);
            residence.SetupProperty(value => value.Ground, (ushort)3);
            ClientHousingRemodel message = CreateRemodel(
                ResidenceId,
                skyWallpaperId: sky.Id,
                musicId: music.Id,
                groundWallpaperId: ground.Id);

            fixture.Map.Remodel(CreateIdentity(ResidenceId), fixture.Player.Object, message);

            Assert.Equal((ushort)sky.Id, residence.Object.Sky);
            Assert.Equal((ushort)music.Id, residence.Object.Music);
            Assert.Equal((ushort)ground.Id, residence.Object.Ground);
        }

        [Fact]
        public void FlagsUpdate_RejectsUnknownFlagsBeforeAnyMutation()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence();
            Allow(residence, fixture.Player, ResidenceModification.Remodel);
            residence.SetupProperty(value => value.Flags, ResidenceFlags.HideGroundClutter);
            residence.SetupProperty(value => value.ResourceSharing, (byte)2);
            residence.SetupProperty(value => value.GardenSharing, (byte)3);
            ClientHousingFlagsUpdate message = CreateFlagsUpdate(
                ResidenceId,
                (ResidenceFlags)0x4,
                4,
                5);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.UpdateResidenceFlags(
                CreateIdentity(ResidenceId),
                fixture.Player.Object,
                message));

            Assert.Equal(ResidenceFlags.HideGroundClutter, residence.Object.Flags);
            Assert.Equal((byte)2, residence.Object.ResourceSharing);
            Assert.Equal((byte)3, residence.Object.GardenSharing);
        }

        [Fact]
        public void CommunityFlagsUpdate_RejectsNeighbourSplitMutation()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence(ResidenceType.Community);
            Allow(residence, fixture.Player, ResidenceModification.Remodel);
            residence.SetupProperty(value => value.Flags, ResidenceFlags.None);
            residence.SetupProperty(value => value.ResourceSharing, (byte)2);
            residence.SetupProperty(value => value.GardenSharing, (byte)3);
            ClientHousingFlagsUpdate message = CreateFlagsUpdate(
                ResidenceId,
                ResidenceFlags.HideNeighbourSkyplots,
                4,
                3);

            Assert.Throws<InvalidPacketValueException>(() => fixture.Map.UpdateResidenceFlags(
                CreateIdentity(ResidenceId),
                fixture.Player.Object,
                message));

            Assert.Equal(ResidenceFlags.None, residence.Object.Flags);
            Assert.Equal((byte)2, residence.Object.ResourceSharing);
        }

        [Fact]
        public void CommunityFlagsUpdate_AllowsKnownFlagsWithUnchangedSplits()
        {
            var fixture = new MapFixture();
            Mock<IResidence> residence = fixture.AddResidence(ResidenceType.Community);
            Allow(residence, fixture.Player, ResidenceModification.Remodel);
            residence.SetupProperty(value => value.Flags, ResidenceFlags.None);
            residence.SetupProperty(value => value.ResourceSharing, (byte)2);
            residence.SetupProperty(value => value.GardenSharing, (byte)3);
            ResidenceFlags flags = ResidenceFlags.HideGroundClutter
                | ResidenceFlags.HideNeighbourSkyplots;
            ClientHousingFlagsUpdate message = CreateFlagsUpdate(
                ResidenceId,
                flags,
                2,
                3);

            fixture.Map.UpdateResidenceFlags(
                CreateIdentity(ResidenceId),
                fixture.Player.Object,
                message);

            Assert.Equal(flags, residence.Object.Flags);
            Assert.Equal((byte)2, residence.Object.ResourceSharing);
            Assert.Equal((byte)3, residence.Object.GardenSharing);
            residence.Verify(value => value.CanModifyResidence(
                fixture.Player.Object,
                ResidenceModification.Remodel), Times.Once);
        }

        private static void Allow(
            Mock<IResidence> residence,
            Mock<IPlayer> player,
            ResidenceModification modification)
        {
            residence
                .Setup(value => value.CanModifyResidence(player.Object, modification))
                .Returns(true);
        }

        private static Mock<IDecor> CreateDecor(
            ulong decorId,
            DecorType type,
            HousingDecorInfoEntry entry = null)
        {
            var decor = new Mock<IDecor>();
            decor.SetupAllProperties();
            decor.SetupGet(value => value.DecorId).Returns(decorId);
            decor.Object.Type = type;
            decor.SetupGet(value => value.Entry).Returns(entry ?? new HousingDecorInfoEntry
            {
                MinScale = 0.2f,
                MaxScale = 2f
            });
            decor.SetupGet(value => value.PendingCreate).Returns(false);
            decor
                .Setup(value => value.Build())
                .Returns(new ServerHousingResidenceDecor.Decor
                {
                    ResidenceIdentity = new NexusForever.Network.World.Message.Model.Shared.Identity
                    {
                        RealmId = RealmId,
                        Id      = ResidenceId
                    },
                    DecorId = decorId
                });
            return decor;
        }

        private static ClientHousingDecorUpdate CreateDecorUpdate(
            DecorUpdateOperation operation,
            params DecorInfo[] decorInfos)
        {
            var message = new ClientHousingDecorUpdate();
            SetProperty(message, nameof(ClientHousingDecorUpdate.Operation), operation);
            foreach (DecorInfo decorInfo in decorInfos)
            {
                var update = new DecorUpdate();
                SetProperty(update, nameof(DecorUpdate.DecorInfo), decorInfo);
                message.DecorUpdates.Add(update);
            }

            return message;
        }

        private static DecorInfo CreateDecorInfo(
            ulong residenceId,
            ulong decorId = 0ul,
            uint decorInfoId = 0u,
            DecorType decorType = DecorType.FreePlace,
            ushort colourShiftId = 0,
            Vector3? position = null,
            uint plotIndex = 0u,
            float scale = 1f)
        {
            var info = new DecorInfo();
            info.TargetResidence.RealmId = RealmId;
            info.TargetResidence.Id = residenceId;
            SetProperty(info, nameof(DecorInfo.DecorId), decorId);
            SetProperty(info, nameof(DecorInfo.DecorInfoId), decorInfoId);
            SetProperty(info, nameof(DecorInfo.DecorType), decorType);
            SetProperty(info, nameof(DecorInfo.ColourShiftId), colourShiftId);
            SetProperty(info, nameof(DecorInfo.PlotIndex), plotIndex);
            SetProperty(info, nameof(DecorInfo.Scale), scale);
            SetProperty(info, nameof(DecorInfo.Position), position ?? Vector3.One);
            SetProperty(info, nameof(DecorInfo.Rotation), Quaternion.Identity);
            return info;
        }

        private static ClientHousingRemodel CreateRemodel(
            ulong residenceId,
            uint wallpaperId = 0u,
            uint roofDecorInfoId = 0u,
            uint entrywayDecorInfoId = 0u,
            uint doorDecorInfoId = 0u,
            uint skyWallpaperId = 0u,
            uint musicId = 0u,
            uint groundWallpaperId = 0u)
        {
            var message = new ClientHousingRemodel();
            message.TargetResidence.RealmId = RealmId;
            message.TargetResidence.Id = residenceId;
            SetProperty(message, nameof(ClientHousingRemodel.WallpaperId), wallpaperId);
            SetProperty(message, nameof(ClientHousingRemodel.RoofDecorInfoId), roofDecorInfoId);
            SetProperty(message, nameof(ClientHousingRemodel.EntrywayDecorInfoId), entrywayDecorInfoId);
            SetProperty(message, nameof(ClientHousingRemodel.DoorDecorInfoId), doorDecorInfoId);
            SetProperty(message, nameof(ClientHousingRemodel.SkyWallpaperId), skyWallpaperId);
            SetProperty(message, nameof(ClientHousingRemodel.MusicId), musicId);
            SetProperty(message, nameof(ClientHousingRemodel.GroundWallpaperId), groundWallpaperId);
            SetProperty(message, nameof(ClientHousingRemodel.Operation), (byte)1);
            return message;
        }

        private static ClientHousingFlagsUpdate CreateFlagsUpdate(
            ulong residenceId,
            ResidenceFlags flags,
            byte harvestSplit,
            byte gardenSplit)
        {
            var message = new ClientHousingFlagsUpdate();
            message.ResidenceIdentity.RealmId = RealmId;
            message.ResidenceIdentity.Id = residenceId;
            SetProperty(message, nameof(ClientHousingFlagsUpdate.Flags), flags);
            SetProperty(message, nameof(ClientHousingFlagsUpdate.NeighbourHarvestSplit), harvestSplit);
            SetProperty(message, nameof(ClientHousingFlagsUpdate.NeighbourGardenSplit), gardenSplit);
            return message;
        }

        private static Identity CreateIdentity(ulong id)
        {
            return new Identity
            {
                RealmId = RealmId,
                Id      = id
            };
        }

        private static void SetProperty<T>(T target, string name, object value)
        {
            PropertyInfo property = typeof(T).GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(property);
            property.SetValue(target, value);
        }

        private static GameTable<T> CreateGameTable<T>(params T[] entries) where T : class, new()
        {
            var table = (GameTable<T>)RuntimeHelpers.GetUninitializedObject(typeof(GameTable<T>));
            typeof(GameTable<T>).GetProperty(nameof(GameTable<T>.Entries))?.SetValue(table, entries);

            FieldInfo idField = typeof(T).GetFields().First();
            uint maximumId = entries.Select(entry => (uint)idField.GetValue(entry)).DefaultIfEmpty().Max();
            int[] lookup = Enumerable.Repeat(-1, checked((int)maximumId + 1)).ToArray();
            for (int i = 0; i < entries.Length; i++)
                lookup[(uint)idField.GetValue(entries[i])] = i;

            typeof(GameTable<T>)
                .GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, lookup);
            typeof(GameTable<T>)
                .GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(table, new GameTableHeader { MaxId = maximumId + 1ul });
            return table;
        }

        private sealed class MapFixture
        {
            public ResidenceMapInstance Map { get; }
            public Mock<IPlayer> Player { get; } = new();

            public MapFixture(
                HousingDecorInfoEntry[] decorEntries = null,
                ColorShiftEntry[] colourEntries = null,
                HousingWallpaperInfoEntry[] wallpaperEntries = null)
            {
                var gameTableManager = new Mock<IGameTableManager>();
                gameTableManager
                    .SetupGet(value => value.HousingDecorInfo)
                    .Returns(CreateGameTable(decorEntries ?? []));
                gameTableManager
                    .SetupGet(value => value.ColorShift)
                    .Returns(CreateGameTable(colourEntries ?? []));
                gameTableManager
                    .SetupGet(value => value.HousingWallpaperInfo)
                    .Returns(CreateGameTable(wallpaperEntries ?? []));

                IConfiguration configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string>
                    {
                        ["Map:GridUnloadTimer"] = "600"
                    })
                    .Build();
                var sharedConfiguration = new SharedConfiguration(configuration);
                sharedConfiguration.Initialise<TestConfiguration>();

                IServiceProvider previousProvider = LegacyServiceProvider.Provider;
                using ServiceProvider serviceProvider = new ServiceCollection()
                    .AddSingleton(sharedConfiguration)
                    .BuildServiceProvider();
                try
                {
                    LegacyServiceProvider.Provider = serviceProvider;
                    Map = new ResidenceMapInstance(
                        Mock.Of<IEntityFactory>(),
                        Mock.Of<IPublicEventManager>(),
                        Mock.Of<IMapLockManager>(),
                        Mock.Of<IGlobalResidenceManager>(),
                        gameTableManager.Object,
                        Mock.Of<IScriptManager>());
                }
                finally
                {
                    LegacyServiceProvider.Provider = previousProvider;
                }

                Player.SetupGet(value => value.Map).Returns(Map);
            }

            public Mock<IResidence> AddResidence(
                ResidenceType type = ResidenceType.Residence,
                ulong residenceId = ResidenceId)
            {
                var residence = new Mock<IResidence>();
                residence.SetupGet(value => value.Identity).Returns(CreateIdentity(residenceId));
                residence.SetupGet(value => value.Type).Returns(type);
                residence.SetupGet(value => value.IsCommunityResidence).Returns(type == ResidenceType.Community);
                residence.SetupProperty(value => value.Map);
                residence.SetupProperty(value => value.Flags, ResidenceFlags.None);
                residence.SetupProperty(value => value.ResourceSharing, (byte)0);
                residence.SetupProperty(value => value.GardenSharing, (byte)0);
                residence.Setup(value => value.GetChildren()).Returns([]);
                residence.Setup(value => value.GetPlots()).Returns([]);
                residence
                    .Setup(value => value.Build())
                    .Returns(new ServerHousingResidences.Residence
                    {
                        ResidenceIdentity = new NexusForever.Network.World.Message.Model.Shared.Identity
                        {
                            RealmId = RealmId,
                            Id      = residenceId
                        },
                        Name = string.Empty,
                        Type = type
                    });
                Map.Initialise(residence.Object);
                return residence;
            }

            private sealed class TestConfiguration
            {
                public MapConfig Map { get; set; }
            }
        }
    }
}
