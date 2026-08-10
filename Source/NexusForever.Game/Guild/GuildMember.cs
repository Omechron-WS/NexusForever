using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Character;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Character;
using NetworkGuildMember = NexusForever.Network.World.Message.Model.Guild.GuildMember;

namespace NexusForever.Game.Guild
{
    public class GuildMember : IGuildMember
    {
        /// <summary>
        /// Determines which fields need saving for <see cref="IGuildMember"/> when being saved to the database.
        /// </summary>
        [Flags]
        public enum GuildMemberSaveMask
        {
            None                     = 0x0000,
            Create                   = 0x0001,
            Delete                   = 0x0002,
            Rank                     = 0x0004,
            Note                     = 0x0008,
            CommunityPlotReservation = 0x0010
        }

        public IGuildBase Guild { get; }
        public Identity PlayerIdentity { get; }
        public ulong CharacterId { get => PlayerIdentity.Id; }

        public IGuildRank Rank
        {
            get => rank;
            set
            {
                rank = value;
                saveMask.Mark(GuildMemberSaveMask.Rank);
            }
        }
        private IGuildRank rank;

        public string Note
        {
            get => note;
            set
            {
                note = value;
                saveMask.Mark(GuildMemberSaveMask.Note);
            }
        }
        private string note;

        public int CommunityPlotReservation
        {
            get => communityPlotReservation;
            set
            {
                communityPlotReservation = value;
                saveMask.Mark(GuildMemberSaveMask.CommunityPlotReservation);
            }
        }
        private int communityPlotReservation;

        private VersionedSaveMask<GuildMemberSaveMask> saveMask = new();

        /// <summary>
        /// Returns if <see cref="IGuildMember"/> is enqueued to be saved to the database.
        /// </summary>
        public bool PendingCreate => (saveMask.Current & GuildMemberSaveMask.Create) != 0;

        /// <summary>
        /// Returns if <see cref="IGuildMember"/> is enqueued to be deleted from the database.
        /// </summary>
        public bool PendingDelete => (saveMask.Current & GuildMemberSaveMask.Delete) != 0;

        /// <summary>
        /// Create a new <see cref="IGuildMember"/> from an existing database model.
        /// </summary>
        public GuildMember(GuildMemberModel model, IGuildBase guild, IGuildRank guildRank)
        {
            Guild                    = guild;
            PlayerIdentity           = new Identity{ Id = model.CharacterId, RealmId = RealmContext.Instance.RealmId };
            rank                     = guildRank;
            note                     = model.Note;
            communityPlotReservation = model.CommunityPlotReservation;

            saveMask = new VersionedSaveMask<GuildMemberSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="IGuildMember"/> from the supplied member information.
        /// </summary>
        public GuildMember(IGuildBase guild, ulong characterId, IGuildRank guildRank, string note = "")
        {
            Guild                    = guild;
            PlayerIdentity           = new Identity { Id = characterId, RealmId = RealmContext.Instance.RealmId };
            rank                     = guildRank;
            this.note                = note;
            communityPlotReservation = -1;

            saveMask = new VersionedSaveMask<GuildMemberSaveMask>(GuildMemberSaveMask.Create);
        }

        /// <summary>
        /// Create a new <see cref="IGuildMember"/> from the supplied member information.
        /// </summary>
        public GuildMember(IGuildBase guild, Identity playerIdentity, IGuildRank guildRank, string note = "")
        {
            Guild                    = guild;
            PlayerIdentity           = playerIdentity;
            rank                     = guildRank;
            this.note                = note;
            communityPlotReservation = -1;

            saveMask = new VersionedSaveMask<GuildMemberSaveMask>(GuildMemberSaveMask.Create);
        }

        /// <summary>
        /// Save this <see cref="IGuildMember"/> to a <see cref="GuildMemberModel"/>
        /// </summary>
        public void Save(CharacterContext context)
        {
            Save(context, action => action(), null);
        }

        /// <summary>
        /// Stage this guild member's database changes and register their successful-commit acknowledgements.
        /// </summary>
        /// <param name="context">Character database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            Save(context, commitScope, null);
        }

        /// <summary>
        /// Stage this guild member's database changes and register their successful-commit acknowledgements.
        /// </summary>
        /// <param name="context">Character database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        /// <param name="deleteAcknowledged">Action invoked when a requested deletion commits.</param>
        public void Save(CharacterContext context, ISaveCommitScope commitScope, Action deleteAcknowledged)
        {
            ArgumentNullException.ThrowIfNull(commitScope);
            Save(context, commitScope.Register, deleteAcknowledged);
        }

        private void Save(CharacterContext context, Action<Action> registerAcknowledgement, Action deleteAcknowledged)
        {
            VersionedSaveMaskSnapshot<GuildMemberSaveMask> snapshot = saveMask.Capture();
            GuildMemberSaveMask stagedMask = snapshot.Mask;
            if (stagedMask == GuildMemberSaveMask.None)
                return;

            void Acknowledge()
            {
                bool deleteStillRequested = PendingDelete;
                saveMask.Acknowledge(snapshot);

                if ((stagedMask & GuildMemberSaveMask.Delete) == 0)
                    return;

                if (deleteStillRequested)
                    deleteAcknowledged?.Invoke();
                else
                    saveMask.Mark(GuildMemberSaveMask.Create);
            }

            if ((stagedMask & (GuildMemberSaveMask.Create | GuildMemberSaveMask.Delete)) ==
                (GuildMemberSaveMask.Create | GuildMemberSaveMask.Delete))
            {
                registerAcknowledgement(Acknowledge);
                return;
            }

            var model = new GuildMemberModel
            {
                Id          = Guild.Id,
                CharacterId = CharacterId
            };

            if ((stagedMask & GuildMemberSaveMask.Create) != 0)
            {
                model.Rank                     = rank.Index;
                model.Note                     = note;
                model.CommunityPlotReservation = communityPlotReservation;
                context.Add(model);
            }
            else if ((stagedMask & GuildMemberSaveMask.Delete) != 0)
                context.Remove(model);
            else
            {
                EntityEntry<GuildMemberModel> entity = context.Attach(model);
                if ((stagedMask & GuildMemberSaveMask.Rank) != 0)
                {
                    model.Rank = rank.Index;
                    entity.Property(p => p.Rank).IsModified = true;
                }
                if ((stagedMask & GuildMemberSaveMask.Note) != 0)
                {
                    model.Note = note;
                    entity.Property(p => p.Note).IsModified = true;
                }
                if ((stagedMask & GuildMemberSaveMask.CommunityPlotReservation) != 0)
                {
                    model.CommunityPlotReservation = communityPlotReservation;
                    entity.Property(p => p.CommunityPlotReservation).IsModified = true;
                }
            }

            registerAcknowledgement(Acknowledge);
        }

        /// <summary>
        /// Return a <see cref="NetworkGuildMember"/> packet of this <see cref="IGuildMember"/>
        /// </summary>
        public NetworkGuildMember Build()
        {
            ICharacter characterInfo = CharacterManager.Instance.GetCharacter(CharacterId);

            return new NetworkGuildMember
            {
                PlayerIdentity           = PlayerIdentity.ToNetworkIdentity(),
                Rank                     = rank.Index,
                Name                     = characterInfo.Name,
                Sex                      = characterInfo.Sex,
                Class                    = characterInfo.Class,
                Path                     = characterInfo.Path,
                Level                    = characterInfo.Level,
                Note                     = Note,
                LastLogoutTimeDays       = characterInfo.GetOnlineStatus() ?? 0f,
                CommunityReservedPlotIndex = communityPlotReservation
            };
        }

        /// <summary>
        /// Enqueue <see cref="IGuildMember"/> to be deleted from the database.
        /// </summary>
        public void EnqueueDelete(bool set)
        {
            if (set)
                saveMask.Mark(GuildMemberSaveMask.Delete);
            else
                saveMask.Clear(GuildMemberSaveMask.Delete);
        }
    }
}
