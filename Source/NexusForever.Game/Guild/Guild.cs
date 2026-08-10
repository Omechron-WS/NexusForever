using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Achievement;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Achievement;
using NexusForever.Game.Static.Guild;
using NexusForever.Network.Internal;
using NexusForever.Network.World.Message.Model.Guild;

namespace NexusForever.Game.Guild
{
    public partial class Guild : GuildBase, IGuild
    {
        /// <summary>
        /// Determines which fields need saving for <see cref="IGuild"/> when being saved to the database.
        /// </summary>
        [Flags]
        public enum GuildSaveMask
        {
            None            = 0x0000,
            MessageOfTheDay = 0x0001,
            AdditionalInfo  = 0x0002
        }

        public override GuildType Type => GuildType.Guild;
        public override uint MaxMembers => 40u;

        public IGuildStandard Standard { get; set; }
        public IGuildAchievementManager AchievementManager { get; private set; }

        public string MessageOfTheDay
        {
            get => messageOfTheDay;
            set
            {
                messageOfTheDay = value;
                saveMask.Mark(GuildSaveMask.MessageOfTheDay);
            }
        }
        private string messageOfTheDay;

        public string AdditionalInfo
        {
            get => additionalInfo;
            set
            {
                additionalInfo = value;
                saveMask.Mark(GuildSaveMask.AdditionalInfo);
            }
        }
        private string additionalInfo;

        private VersionedSaveMask<GuildSaveMask> saveMask = new();

        #region Dependency Injection

        public Guild(
            IRealmContext realmContext,
            IInternalMessagePublisher messagePublisher)
            : base(realmContext, messagePublisher)
        {
        }

        #endregion

        /// <summary>
        /// Create a new <see cref="IGuild"/> from an existing database model.
        /// </summary>
        public override void Initialise(GuildModel model)
        {
            Standard           = new GuildStandard(model.GuildData);
            AchievementManager = new GuildAchievementManager(this, model);
            messageOfTheDay    = model.GuildData.MessageOfTheDay;
            additionalInfo     = model.GuildData.AdditionalInfo;

            base.Initialise(model);
            saveMask = new VersionedSaveMask<GuildSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="IGuild"/> using the supplied parameters.
        /// </summary>
        public void Initialise(string name, string leaderRankName, string councilRankName, string memberRankName, IGuildStandard standard)
        {
            Standard           = standard;
            AchievementManager = new GuildAchievementManager(this);
            messageOfTheDay    = "";
            additionalInfo     = "";

            Initialise(name, leaderRankName, councilRankName, memberRankName);
            saveMask = new VersionedSaveMask<GuildSaveMask>();
        }

        protected override void StageSave(CharacterContext context, GuildBaseSaveMask baseSaveMask, Action<Action> registerAcknowledgement)
        {
            if ((baseSaveMask & GuildBaseSaveMask.Create) != 0)
            {
                context.Add(new GuildDataModel
                {
                    Id                   = Id,
                    AdditionalInfo       = AdditionalInfo,
                    MessageOfTheDay      = MessageOfTheDay,
                    BackgroundIconPartId = (ushort)Standard.BackgroundIcon.GuildStandardPartEntry.Id,
                    ForegroundIconPartId = (ushort)Standard.ForegroundIcon.GuildStandardPartEntry.Id,
                    ScanLinesPartId      = (ushort)Standard.ScanLines.GuildStandardPartEntry.Id
                });
            }

            VersionedSaveMaskSnapshot<GuildSaveMask> snapshot = saveMask.Capture();
            GuildSaveMask stagedMask = snapshot.Mask;
            if ((baseSaveMask & GuildBaseSaveMask.Create) == 0 && stagedMask != GuildSaveMask.None)
            {
                var model = new GuildDataModel
                {
                    Id = Id
                };

                EntityEntry<GuildDataModel> entity = context.Attach(model);
                if ((stagedMask & GuildSaveMask.MessageOfTheDay) != 0)
                {
                    model.MessageOfTheDay = MessageOfTheDay;
                    entity.Property(p => p.MessageOfTheDay).IsModified = true;
                }

                if ((stagedMask & GuildSaveMask.AdditionalInfo) != 0)
                {
                    model.AdditionalInfo = AdditionalInfo;
                    entity.Property(p => p.AdditionalInfo).IsModified = true;
                }

            }

            registerAcknowledgement(() => saveMask.Acknowledge(snapshot));

            AchievementManager.Save(context, new DelegateSaveCommitScope(registerAcknowledgement));
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

        public override GuildData Build()
        {
            return new GuildData
            {
                GuildId           = Id,
                GuildName         = Name,
                Flags             = Flags,
                Type              = Type,
                Ranks             = GetGuildRanksPackets().ToList(),
                GuildStandard     = Standard.Build(),
                MemberCount       = (uint)members.Count,
                OnlineMemberCount = (uint)onlineMembers.Count,
                GuildInfo =
                {
                    MessageOfTheDay         = MessageOfTheDay,
                    GuildInfo               = AdditionalInfo,
                    GuildCreationDateInDays = (float)DateTime.Now.Subtract(CreateTime).TotalDays * -1f
                }
            };
        }

        /// <summary>
        /// Set if taxes are enabled for <see cref="IGuild"/>.
        /// </summary>
        public void SetTaxes(bool enabled)
        {
            if (enabled)
                SetFlag(GuildFlag.Taxes);
            else
                RemoveFlag(GuildFlag.Taxes);

            SendGuildFlagUpdate();
        }
    }
}
