using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Static.Guild;
using NexusForever.Game.Static.Housing;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model.Housing;
using NexusForever.Shared;

namespace NexusForever.Game.Housing
{
    public class Residence : IResidence
    {
        /// <summary>
        /// Determines which fields need saving for <see cref="IResidence"/> when being saved to the database.
        /// </summary>
        [Flags]
        private enum ResidenceSaveMask
        {
            None            = 0x000000,
            Create          = 0x000001,
            Name            = 0x000002,
            Wallpaper       = 0x000004,
            Roof            = 0x000008,
            Entryway        = 0x000010,
            Door            = 0x000020,
            Ground          = 0x000040,
            Sky             = 0x000080,
            Flags           = 0x000100,
            ResourceSharing = 0x000200,
            GardenSharing   = 0x000400,
            Decor           = 0x000800,
            Plot            = 0x001000,
            PrivacyLevel    = 0x002000,
            Music           = 0x004000,
            PropertyInfo    = 0x008000,
            GuildOwner      = 0x010000
        }

        public Identity Identity { get; private set; }
        public ResidenceType Type { get; private set; }
        public Identity OwnerIdentity { get; private set; }

        public Identity GuildOwnerIdentity
        {
            get => guildOwnerIdentity;
            set
            {
                guildOwnerIdentity = value;
                saveMask.Mark(ResidenceSaveMask.GuildOwner);
            }
        }

        private Identity guildOwnerIdentity;

        public PropertyInfoId PropertyInfoId
        {
            get => propertyInfoId;
            set
            {
                propertyInfoId = value;
                saveMask.Mark(ResidenceSaveMask.PropertyInfo);

                UpdatePlots();
            }
        }

        private PropertyInfoId propertyInfoId;

        public string Name
        {
            get => name;
            set
            {
                name = value;
                saveMask.Mark(ResidenceSaveMask.Name);
            }
        }

        private string name;

        public ResidencePrivacyLevel PrivacyLevel
        {
            get => privacyLevel;
            set
            {
                if (value < ResidencePrivacyLevel.Public || value > ResidencePrivacyLevel.Private)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown residence privacy level.");

                if (privacyLevel == value)
                    return;

                privacyLevel = value;
                saveMask.Mark(ResidenceSaveMask.PrivacyLevel);
            }
        }

        private ResidencePrivacyLevel privacyLevel;

        public ushort Wallpaper
        {
            get => wallpaperId;
            set
            {
                if (gameTableManager.HousingWallpaperInfo.GetEntry(value) == null)
                    throw new ArgumentOutOfRangeException();

                wallpaperId = value;
                saveMask.Mark(ResidenceSaveMask.Wallpaper);
            }
        }

        private ushort wallpaperId;

        public ushort Roof
        {
            get => roofDecorInfoId;
            set
            {
                if (gameTableManager.HousingDecorInfo.GetEntry(value) == null)
                    throw new ArgumentOutOfRangeException();

                roofDecorInfoId = value;
                saveMask.Mark(ResidenceSaveMask.Roof);
            }
        }

        private ushort roofDecorInfoId;

        public ushort Entryway
        {
            get => entrywayDecorInfoId;
            set
            {
                if (gameTableManager.HousingDecorInfo.GetEntry(value) == null)
                    throw new ArgumentOutOfRangeException();

                entrywayDecorInfoId = value;
                saveMask.Mark(ResidenceSaveMask.Entryway);
            }
        }

        private ushort entrywayDecorInfoId;

        public ushort Door
        {
            get => doorDecorInfoId;
            set
            {
                if (gameTableManager.HousingDecorInfo.GetEntry(value) == null)
                    throw new ArgumentOutOfRangeException();

                doorDecorInfoId = value;
                saveMask.Mark(ResidenceSaveMask.Door);
            }
        }

        private ushort doorDecorInfoId;

        public ushort Music
        {
            get => musicId;
            set
            {
                HousingWallpaperInfoEntry entry = gameTableManager.HousingWallpaperInfo.GetEntry(value);
                if (entry == null)
                    throw new ArgumentOutOfRangeException();

                if ((entry.Flags & 0x100) == 0)
                    throw new ArgumentOutOfRangeException();

                musicId = value;
                saveMask.Mark(ResidenceSaveMask.Music);
            }
        }

        private ushort musicId;

        public ushort Ground
        {
            get => groundWallpaperId;
            set
            {
                HousingWallpaperInfoEntry entry = gameTableManager.HousingWallpaperInfo.GetEntry(value);
                if (entry == null)
                    throw new ArgumentOutOfRangeException();

                if ((entry.Flags & 0x200) == 0)
                    throw new ArgumentOutOfRangeException();

                groundWallpaperId = value;
                saveMask.Mark(ResidenceSaveMask.Ground);
            }
        }

        private ushort groundWallpaperId;

        public ushort Sky
        {
            get => skyWallpaperId;
            set
            {
                HousingWallpaperInfoEntry entry = gameTableManager.HousingWallpaperInfo.GetEntry(value);
                if (entry == null)
                    throw new ArgumentOutOfRangeException();

                if ((entry.Flags & 0x40) == 0)
                    throw new ArgumentOutOfRangeException();

                skyWallpaperId = value;
                saveMask.Mark(ResidenceSaveMask.Sky);
            }
        }

        private ushort skyWallpaperId;

        public ResidenceFlags Flags
        {
            get => flags;
            set
            {
                flags = value;
                saveMask.Mark(ResidenceSaveMask.Flags);
            }
        }

        private ResidenceFlags flags;

        public byte ResourceSharing
        {
            get => resourceSharing;
            set
            {
                resourceSharing = value;
                saveMask.Mark(ResidenceSaveMask.ResourceSharing);
            }
        }

        private byte resourceSharing;

        public byte GardenSharing
        {
            get => gardenSharing;
            set
            {
                gardenSharing = value;
                saveMask.Mark(ResidenceSaveMask.GardenSharing);
            }
        }

        private byte gardenSharing;

        private VersionedSaveMask<ResidenceSaveMask> saveMask = new();

        public bool IsCommunityResidence => GuildOwnerIdentity != null && OwnerIdentity == null;

        /// <summary>
        /// <see cref="IResidenceMapInstance"/> this <see cref="IResidence"/> resides on.
        /// </summary>
        /// <remarks>
        /// This can either be an individual or shared residencial map.
        /// </remarks>
        public IResidenceMapInstance Map { get; set; }

        /// <summary>
        /// Parent <see cref="IResidence"/> for this <see cref="IResidence"/>.
        /// </summary>
        /// <remarks>
        /// This will be set if the <see cref="IResidence"/> is part of a <see cref="ICommunity"/>.
        /// </remarks>
        public IResidence Parent { get; set; }

        /// <summary>
        /// A collection of child <see cref="IResidence"/> for this <see cref="IResidence"/>.
        /// </summary>
        /// <remarks>
        /// This will contain entries if the <see cref="IResidence"/> is the parent for a <see cref="ICommunity"/>.
        /// </remarks>
        private readonly Dictionary<Identity, IResidenceChild> children = [];

        private readonly Dictionary<ulong, IDecor> decors = [];
        private readonly List<IPlot> plots = [];

        #region Dependency Injection

        private readonly IRealmContext realmContext;
        private readonly IGameTableManager gameTableManager;
        private readonly IGlobalResidenceManager globalResidenceManager;
        private readonly IFactory<IPlot> plotFactory;

        public Residence(
            IRealmContext realmContext,
            IGameTableManager gameTableManager,
            IGlobalResidenceManager globalResidenceManager,
            IFactory<IPlot> plotFactory)
        {
            this.realmContext           = realmContext;
            this.gameTableManager       = gameTableManager;
            this.globalResidenceManager = globalResidenceManager;
            this.plotFactory            = plotFactory;
        }

        #endregion

        /// <summary>
        /// Initialise a new <see cref="IResidence"/> from an existing database model.
        /// </summary>
        public void Initialise(ResidenceModel model)
        {
            if (Identity != null)
                throw new InvalidOperationException("Residence is already initialised.");

            if (model.PrivacyLevel < ResidencePrivacyLevel.Public || model.PrivacyLevel > ResidencePrivacyLevel.Private)
                throw new DatabaseDataException($"Residence {model.Id} has invalid privacy level {(int)model.PrivacyLevel}!");

            Identity = new Identity
            {
                RealmId = realmContext.RealmId,
                Id      = model.Id
            };
            OwnerIdentity = model.OwnerId != null ? new Identity
            {
                RealmId = realmContext.RealmId,
                Id      = model.OwnerId.Value,
            } : null;
            GuildOwnerIdentity = model.GuildOwnerId != null ? new Identity
            {
                RealmId = realmContext.RealmId,
                Id      = model.GuildOwnerId.Value,
            } : null;
            propertyInfoId      = model.PropertyInfoId;
            name                = model.Name;
            privacyLevel        = model.PrivacyLevel;
            wallpaperId         = model.WallpaperId;
            roofDecorInfoId     = model.RoofDecorInfoId;
            entrywayDecorInfoId = model.EntrywayDecorInfoId;
            doorDecorInfoId     = model.DoorDecorInfoId;
            groundWallpaperId   = model.GroundWallpaperId;
            musicId             = model.MusicId;
            skyWallpaperId      = model.SkyWallpaperId;
            flags               = model.Flags;
            resourceSharing     = model.ResourceSharing;
            gardenSharing       = model.GardenSharing;

            // community residences are owned by only a guild
            Type = model.OwnerId.HasValue ? ResidenceType.Residence : ResidenceType.Community;

            foreach (ResidenceDecor decorModel in model.Decor)
            {
                HousingDecorInfoEntry entry = GameTableManager.Instance.HousingDecorInfo.GetEntry(decorModel.DecorInfoId);
                if (entry == null)
                    throw new DatabaseDataException($"Decor {decorModel.Id} has invalid decor entry {decorModel.DecorInfoId}!");

                var decor = new Decor(this, decorModel, entry);
                decors.Add(decor.DecorId, decor);
            }

            foreach (ResidencePlotModel plotModel in model.Plot
                .OrderBy(e => e.Index))
            {
                IPlot plot = plotFactory.Resolve();
                plot.Initialise(plotModel);
                plots.Add(plot);
            }

            saveMask = new VersionedSaveMask<ResidenceSaveMask>();
        }

        /// <summary>
        /// Initialise a new <see cref="IResidence"/> from a <see cref="IPlayer"/>.
        /// </summary>
        public void Initialise(IPlayer player)
        {
            if (Identity != null)
                throw new InvalidOperationException("Residence is already initialised.");

            Identity       = globalResidenceManager.NextResidenceId;
            Type           = ResidenceType.Residence;
            OwnerIdentity  = player.Identity;
            propertyInfoId = PropertyInfoId.Residence;
            name           = $"{player.Name}'s House";
            privacyLevel   = ResidencePrivacyLevel.Public;

            saveMask       = new VersionedSaveMask<ResidenceSaveMask>(ResidenceSaveMask.Create);

            InitialiseDefaultPlots();

            // TODO: find a better way to do this, this adds the construction yard plug
            plots[0].SetPlug(531);
        }

        /// <summary>
        /// Create a new <see cref="IResidence"/> for a <see cref="ICommunity"/>.
        /// </summary>
        /// <remarks>
        /// This creates the parent <see cref="IResidence"/> which all children are part of.
        /// </remarks>
        public void Initialise(ICommunity community)
        {
            if (Identity != null)
                throw new InvalidOperationException("Residence is already initialised.");

            Identity       = globalResidenceManager.NextResidenceId;
            Type           = ResidenceType.Community;
            GuildOwnerIdentity = community.Identity;
            propertyInfoId = PropertyInfoId.Community;
            name           = community.Name;
            privacyLevel   = ResidencePrivacyLevel.Public;

            saveMask       = new VersionedSaveMask<ResidenceSaveMask>(ResidenceSaveMask.Create);

            InitialiseDefaultPlots();

            // TODO: find a better way to do this
            plots[0].SetPlug(573);
        }

        private void InitialiseDefaultPlots()
        {
            foreach (HousingPlotInfoEntry entry in GameTableManager.Instance.HousingPlotInfo.Entries
                .Where(e => (PropertyInfoId)e.HousingPropertyInfoId == PropertyInfoId)
                .OrderBy(e => e.HousingPropertyPlotIndex))
            {
                IPlot plot = plotFactory.Resolve();
                plot.Initialise(Identity.Id, entry);
                plots.Add(plot);
            }
        }

        private void UpdatePlots()
        {
            foreach (HousingPlotInfoEntry entry in GameTableManager.Instance.HousingPlotInfo.Entries
                .Where(e => (PropertyInfoId)e.HousingPropertyInfoId == PropertyInfoId))
                GetPlot((byte)entry.HousingPropertyPlotIndex).PlotInfoEntry = entry;
        }

        public void Save(CharacterContext context)
        {
            Save(context, action => action());
        }

        /// <summary>
        /// Stage this residence's database changes and register their successful-commit acknowledgements.
        /// </summary>
        /// <param name="context">Character database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(commitScope);
            Save(context, commitScope.Register);
        }

        private void Save(CharacterContext context, Action<Action> registerAcknowledgement)
        {
            VersionedSaveMaskSnapshot<ResidenceSaveMask> snapshot = saveMask.Capture();
            ResidenceSaveMask stagedMask = snapshot.Mask;
            if (stagedMask != ResidenceSaveMask.None)
            {
                if ((stagedMask & ResidenceSaveMask.Create) != 0)
                {
                    // residence doesn't exist in database, all information must be saved
                    context.Add(new ResidenceModel
                    {
                        Id                  = Identity.Id,
                        OwnerId             = OwnerIdentity?.Id,
                        GuildOwnerId        = GuildOwnerIdentity?.Id,
                        PropertyInfoId      = PropertyInfoId,
                        Name                = Name,
                        PrivacyLevel        = privacyLevel,
                        WallpaperId         = wallpaperId,
                        RoofDecorInfoId     = roofDecorInfoId,
                        EntrywayDecorInfoId = entrywayDecorInfoId,
                        DoorDecorInfoId     = doorDecorInfoId,
                        GroundWallpaperId   = groundWallpaperId,
                        MusicId             = musicId,
                        SkyWallpaperId      = skyWallpaperId,
                        Flags               = flags,
                        ResourceSharing     = resourceSharing,
                        GardenSharing       = gardenSharing
                    });
                }
                else
                {
                    // residence already exists in database, save only data that has been modified
                    var model = new ResidenceModel
                    {
                        Id = Identity.Id
                    };

                    // could probably clean this up with reflection, works for the time being
                    EntityEntry<ResidenceModel> entity = context.Attach(model);
                    if ((stagedMask & ResidenceSaveMask.Name) != 0)
                    {
                        model.Name = Name;
                        entity.Property(p => p.Name).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.PrivacyLevel) != 0)
                    {
                        model.PrivacyLevel = PrivacyLevel;
                        entity.Property(p => p.PrivacyLevel).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.Wallpaper) != 0)
                    {
                        model.WallpaperId = Wallpaper;
                        entity.Property(p => p.WallpaperId).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.Roof) != 0)
                    {
                        model.RoofDecorInfoId = Roof;
                        entity.Property(p => p.RoofDecorInfoId).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.Entryway) != 0)
                    {
                        model.EntrywayDecorInfoId = Entryway;
                        entity.Property(p => p.EntrywayDecorInfoId).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.Door) != 0)
                    {
                        model.DoorDecorInfoId = Door;
                        entity.Property(p => p.DoorDecorInfoId).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.Ground) != 0)
                    {
                        model.GroundWallpaperId = Ground;
                        entity.Property(p => p.GroundWallpaperId).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.Music) != 0)
                    {
                        model.MusicId = Music;
                        entity.Property(p => p.MusicId).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.Sky) != 0)
                    {
                        model.SkyWallpaperId = Sky;
                        entity.Property(p => p.SkyWallpaperId).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.Flags) != 0)
                    {
                        model.Flags = Flags;
                        entity.Property(p => p.Flags).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.ResourceSharing) != 0)
                    {
                        model.ResourceSharing = ResourceSharing;
                        entity.Property(p => p.ResourceSharing).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.GardenSharing) != 0)
                    {
                        model.GardenSharing = GardenSharing;
                        entity.Property(p => p.GardenSharing).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.GuildOwner) != 0)
                    {
                        model.GuildOwnerId = GuildOwnerIdentity?.Id;
                        entity.Property(p => p.GuildOwnerId).IsModified = true;
                    }
                    if ((stagedMask & ResidenceSaveMask.PropertyInfo) != 0)
                    {
                        model.PropertyInfoId = PropertyInfoId;
                        entity.Property(p => p.PropertyInfoId).IsModified = true;
                    }
                }

            }

            registerAcknowledgement(() => saveMask.Acknowledge(snapshot));

            var commitScope = new DelegateSaveCommitScope(registerAcknowledgement);
            foreach (IDecor decor in decors.Values.ToList())
            {
                decor.Save(context, commitScope, () =>
                {
                    if (decors.TryGetValue(decor.DecorId, out IDecor current) && ReferenceEquals(current, decor))
                        decors.Remove(decor.DecorId);
                });
            }

            foreach (IPlot plot in plots)
                plot.Save(context, commitScope);
        }

        private sealed class DelegateSaveCommitScope : ISaveCommitScope
        {
            private readonly Action<Action> registerAcknowledgement;

            public DelegateSaveCommitScope(Action<Action> registerAcknowledgement)
            {
                this.registerAcknowledgement = registerAcknowledgement;
            }

            public void Register(Action action)
            {
                registerAcknowledgement(action);
            }
        }

        public ServerHousingResidences.Residence Build()
        {
            return new()
            {
                ResidenceIdentity     = Identity.ToNetworkIdentity(),
                NeighbourhoodId       = 0x190000000000000A/*GuildOwnerId.GetValueOrDefault(0ul)*/,
                CharacterIdOwner      = OwnerIdentity.Id,
                GuildIdOwner          = Type == ResidenceType.Community ? GuildOwnerIdentity.Id : 0,
                Type                  = Type,
                Name                  = Name,
                PropertyInfoId        = PropertyInfoId,
                WallpaperExterior     = Wallpaper,
                Entryway              = Entryway,
                Roof                  = Roof,
                Door                  = Door,
                Ground                = Ground,
                Music                 = Music,
                Sky                   = Sky,
                Flags                 = Flags,
                NeighbourHarvestSplit = ResourceSharing,
                NeighbourGardenSplit  = GardenSharing
            };
        }

        /// <summary>
        /// Return all <see cref="IResidenceChild"/>'s.
        /// </summary>
        /// <remarks>
        /// Only community residences will have child residences.
        /// </remarks>
        public IEnumerable<IResidenceChild> GetChildren()
        {
            return children.Values;
        }

        /// <summary>
        /// Return <see cref="IResidenceChild"/> with supplied property info id.
        /// </summary>
        /// <remarks>
        /// Only community residences will have child residences.
        /// </remarks>
        public IResidenceChild GetChild(PropertyInfoId propertyInfoId)
        {
            return children.Values
                .SingleOrDefault(c => c.Residence.PropertyInfoId == propertyInfoId);
        }

        /// <summary>
        /// Return <see cref="IResidenceChild"/> with supplied character id.
        /// </summary>
        /// <remarks>
        /// Only community residences will have child residences.
        /// </remarks>
        public IResidenceChild GetChild(Identity playerIdentity)
        {
            return children.Values
                .SingleOrDefault(c => c.Residence.OwnerIdentity == playerIdentity);
        }

        /// <summary>
        /// Add child <see cref="IResidence"/> to parent <see cref="IResidence"/>.
        /// </summary>
        /// <remarks>
        /// Child residences can only be added to a community.
        /// </remarks>
        public void AddChild(IResidence residence, bool temporary)
        {
            if (Type != ResidenceType.Community)
                throw new InvalidOperationException("Only community residences can have children!");

            if (children.Any(c => c.Value.Residence.PropertyInfoId == residence.PropertyInfoId))
                throw new ArgumentException();

            children.Add(residence.Identity, new ResidenceChild
            {
                Residence   = residence,
                IsTemporary = temporary
            });

            residence.Parent       = this;
            residence.GuildOwnerIdentity = GuildOwnerIdentity;
        }

        /// <summary>
        /// Remove child <see cref="IResidence"/> to parent <see cref="IResidence"/>.
        /// </summary>
        /// <remarks>
        /// Child residences can only be removed from a community.
        /// </remarks>
        public void RemoveChild(IResidence residence)
        {
            if (Type != ResidenceType.Community)
                throw new InvalidOperationException("Only community residences can have children!");

            children.Remove(residence.Identity);

            residence.Parent       = null;
            residence.GuildOwnerIdentity = null;
        }

        /// <summary>
        /// Returns true if <see cref="IPlayer"/> can modify the <see cref="IResidence"/>.
        /// </summary>
        /// <remarks>
        /// This is valid for both community and individual residences.
        /// </remarks>
        public bool CanModifyResidence(IPlayer player)
        {
            switch (Type)
            {
                case ResidenceType.Community:
                {
                    ICommunity community = player.GuildManager.GetGuild<ICommunity>(GuildType.Community);
                    if (community == null)
                        return false;

                    IGuildMember member = community.GetMember(player.CharacterId);
                    if (member == null)
                        return false;

                    return member.Rank.HasPermission(GuildRankPermission.DecorateCommunity);
                }
                case ResidenceType.Residence:
                {
                    // TODO: roommates can also update decor
                    return player.Identity == OwnerIdentity;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// Return all <see cref="IPlot"/>'s for the <see cref="IResidence"/>.
        /// </summary>
        public IEnumerable<IPlot> GetPlots()
        {
            return plots;
        }

        /// <summary>
        /// Return all <see cref="IDecor"/> for the <see cref="IResidence"/>.
        /// </summary>
        public IEnumerable<IDecor> GetDecor()
        {
            return decors.Values;
        }

        /// <summary>
        /// Return all <see cref="IDecor"/> placed in the world for the <see cref="IResidence"/>.
        /// </summary>
        public IEnumerable<IDecor> GetPlacedDecor()
        {
            foreach (IDecor decor in decors.Values)
                if (decor.Type != DecorType.Crate)
                    yield return decor;
        }

        /// <summary>
        /// Return <see cref="IDecor"/> with the supplied id.
        /// </summary>
        public IDecor GetDecor(ulong decorId)
        {
            decors.TryGetValue(decorId, out IDecor decor);
            return decor;
        }

        /// <summary>
        /// Create a new <see cref="IDecor"/> from supplied <see cref="HousingDecorInfoEntry"/> for <see cref="IResidence"/>.
        /// </summary>
        public IDecor DecorCreate(HousingDecorInfoEntry entry)
        {
            var decor = new Decor(this, GlobalResidenceManager.Instance.NextDecorId, entry);
            decors.Add(decor.DecorId, decor);
            return decor;
        }

        /// <summary>
        /// Create a new <see cref="IDecor"/> from an existing <see cref="IDecor"/>.
        /// </summary>
        /// <remarks>
        /// Copies all data from the source <see cref="IDecor"/> with a new id.
        /// </remarks>
        public IDecor DecorCopy(IDecor decor)
        {
            var newDecor = new Decor(this, decor, GlobalResidenceManager.Instance.NextDecorId);
            decors.Add(newDecor.DecorId, newDecor);
            return newDecor;
        }

        /// <summary>
        /// Remove existing <see cref="IDecor"/> from the <see cref="IResidence"/>.
        /// </summary>
        /// <remarks>
        /// This does not queue the <see cref="IDecor"/> for deletion from the database.
        /// This is intended to be used for <see cref="IDecor"/> that has yet to be saved to the database.
        /// </remarks>
        public void DecorRemove(IDecor decor)
        {
            decors.Remove(decor.DecorId);
        }

        /// <summary>
        /// Return <see cref="IPlot"/> at the supplied index.
        /// </summary>
        public IPlot GetPlot(byte plotIndex)
        {
            return plots.FirstOrDefault(i => i.Index == plotIndex);
        }

        /// <summary>
        /// Return <see cref="IPlot"/> that matches the supploed Plot Info ID.
        /// </summary>
        public IPlot GetPlot(uint plotInfoId)
        {
            return plots.FirstOrDefault(i => i.PlotInfoEntry.Id == plotInfoId);
        }
    }
}
