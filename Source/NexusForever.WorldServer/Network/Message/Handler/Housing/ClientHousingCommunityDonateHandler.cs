using System.Collections.Generic;
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
    public class ClientHousingCommunityDonateHandler : IMessageHandler<IWorldSession, ClientHousingCommunityDonate>
    {
        public void HandleMessage(IWorldSession session, ClientHousingCommunityDonate housingCommunityDonate)
        {
            // can only donate to a community from a residence map
            if (session.Player.Map is not IResidenceMapInstance residenceMap
                || housingCommunityDonate?.Decor == null)
                throw new InvalidPacketValueException();

            IResidence residence = session.Player.ResidenceManager.Residence;
            if (residence == null
                || residence.Type != ResidenceType.Residence
                || residence.OwnerIdentity != session.Player.Identity
                || residence.Map == null
                || !ReferenceEquals(residence.Map, residenceMap))
                throw new InvalidPacketValueException();

            ICommunity community = session.Player.GuildManager.GetGuild<ICommunity>(GuildType.Community);
            IResidence communityResidence = community?.Residence;
            if (community == null
                || community.PendingDelete
                || communityResidence == null
                || communityResidence.Type != ResidenceType.Community
                || communityResidence.GuildOwnerIdentity != community.Identity
                || !ReferenceEquals(community.Residence, communityResidence))
                throw new InvalidPacketValueException();

            IGuildMember member = community.GetMember(session.Player.CharacterId);
            if (member?.Rank == null)
                throw new InvalidPacketValueException();

            // The remaining DecorInfo fields are not authoritative for a donation. Resolve the
            // complete batch from the authenticated player's personal crate before applying its
            // first mutation so duplicates, stale tombstones, and late invalid entries cannot
            // create partial transfers.
            var preparedDecor = new List<IDecor>(housingCommunityDonate.Decor.Count);
            var decorIds = new HashSet<ulong>();
            foreach (DecorInfo decorInfo in housingCommunityDonate.Decor)
            {
                if (decorInfo == null || !decorIds.Add(decorInfo.DecorId))
                    throw new InvalidPacketValueException();

                IDecor decor = residence.GetDecor(decorInfo.DecorId);
                if (decor == null
                    || decor.DecorId != decorInfo.DecorId
                    || !ReferenceEquals(decor.Residence, residence)
                    || decor.ResidenceIdentity != residence.Identity
                    || decor.Type != DecorType.Crate
                    || decor.PendingDelete)
                    throw new InvalidPacketValueException();

                preparedDecor.Add(decor);
            }

            foreach (IDecor decor in preparedDecor)
            {
                // copy decor to recipient residence
                if (communityResidence.Map != null)
                    communityResidence.Map.DecorCopy(communityResidence, decor);
                else
                    communityResidence.DecorCopy(decor);

                // remove decor from donor residence
                if (residence.Map != null)
                    residence.Map.DecorDelete(residence, decor);
                else
                {
                    if (decor.PendingCreate)
                        residence.DecorRemove(decor);
                    else
                        decor.EnqueueDelete(true);
                }
            }
        }
    }
}
