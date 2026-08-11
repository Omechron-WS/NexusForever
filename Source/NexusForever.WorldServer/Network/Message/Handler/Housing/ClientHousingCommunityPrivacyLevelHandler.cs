using NexusForever.Game;
using NexusForever.Game.Abstract.Character;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Static.Guild;
using NexusForever.Game.Static.Housing;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Housing;

namespace NexusForever.WorldServer.Network.Message.Handler.Housing
{
    public class ClientHousingCommunityPrivacyLevelHandler : IMessageHandler<IWorldSession, ClientHousingCommunityPrivacyLevel>
    {
        #region Dependency Injection

        private readonly IGlobalResidenceManager globalResidenceManager;
        private readonly ICharacterManager characterManager;

        public ClientHousingCommunityPrivacyLevelHandler(
            IGlobalResidenceManager globalResidenceManager,
            ICharacterManager characterManager)
        {
            this.globalResidenceManager = globalResidenceManager;
            this.characterManager       = characterManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientHousingCommunityPrivacyLevel housingCommunityPrivacyLevel)
        {
            if (session.Player.Map is not IResidenceMapInstance)
                throw new InvalidPacketValueException();

            ICommunity community = session.Player.GuildManager.GetGuild<ICommunity>(GuildType.Community);
            if (community?.Residence == null
                || community.Identity != housingCommunityPrivacyLevel.GuildIdentity.ToGameIdentity()
                || community.Residence.Type != ResidenceType.Community
                || community.Residence.GuildOwnerIdentity != community.Identity
                || community.Residence.Map == null
                || !ReferenceEquals(community.Residence.Map, session.Player.Map))
                throw new InvalidPacketValueException();

            IGuildMember member = community.GetMember(session.Player.CharacterId);
            if (member?.Rank == null
                || !member.Rank.HasPermission(GuildRankPermission.ChangeCommunityRemodelOptions))
                throw new InvalidPacketValueException();

            if (housingCommunityPrivacyLevel.PrivacyLevel is not CommunityPrivacyLevel.Public
                and not CommunityPrivacyLevel.Private)
                throw new InvalidPacketValueException();

            if (!community.LeaderId.HasValue)
                throw new InvalidPacketValueException();

            ICharacter leader = characterManager.GetCharacter(community.LeaderId.Value);
            if (leader == null)
                throw new InvalidPacketValueException();

            // Update the derived index before the authoritative flag broadcasts. If broadcasting
            // fails, the flag mutation has already completed and both in-memory states still agree.
            if (housingCommunityPrivacyLevel.PrivacyLevel == CommunityPrivacyLevel.Public)
                globalResidenceManager.RegisterCommunityVisits(community.Residence, community, leader.Name);
            else
                globalResidenceManager.DeregisterCommunityVists(community.Residence.Identity);

            community.SetCommunityPrivate(housingCommunityPrivacyLevel.PrivacyLevel == CommunityPrivacyLevel.Private);
        }
    }
}
