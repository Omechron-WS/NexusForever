using System;
using NexusForever.Game;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Abstract.Housing;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.Map.Lock;
using NexusForever.Game.Map;
using NexusForever.Game.Static.Guild;
using NexusForever.Game.Static.Housing;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Housing;

namespace NexusForever.WorldServer.Network.Message.Handler.Housing
{
    public class ClientHousingVisitHandler : IMessageHandler<IWorldSession, ClientHousingVisit>
    {
        #region Dependency Injection

        private readonly IGlobalResidenceManager globalResidenceManager;
        private readonly IGlobalGuildManager globalGuildManager;
        private readonly IMapLockManager mapLockManager;

        public ClientHousingVisitHandler(
            IGlobalResidenceManager globalResidenceManager,
            IGlobalGuildManager globalGuildManager,
            IMapLockManager mapLockManager)
        {
            this.globalResidenceManager = globalResidenceManager;
            this.globalGuildManager     = globalGuildManager;
            this.mapLockManager         = mapLockManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientHousingVisit housingVisit)
        {
            if (session.Player.Map is not IResidenceMapInstance)
                throw new InvalidPacketValueException();

            if (!session.Player.CanTeleport())
                return;

            IResidence residence;
            if (!string.IsNullOrEmpty(housingVisit.PlayerToVisitName))
                residence = globalResidenceManager.GetResidenceByOwner(housingVisit.PlayerToVisitName);
            else if (!string.IsNullOrEmpty(housingVisit.CommunityToVisitName))
                residence = globalResidenceManager.GetCommunityByOwner(housingVisit.CommunityToVisitName);
            else if (housingVisit.IdentityToVisit.Id != 0ul)
                residence = globalResidenceManager.GetResidenceByOwner(housingVisit.IdentityToVisit.ToGameIdentity());
            else if (housingVisit.CommunityToVisitIdentity.Id != 0ul)
            {
                Identity communityIdentity = housingVisit.CommunityToVisitIdentity.ToGameIdentity();
                ICommunity community = globalGuildManager.GetGuild(communityIdentity) as ICommunity;
                residence = community?.Identity == communityIdentity ? community.Residence : null;
            }
            else
                throw new InvalidPacketValueException();

            if (residence == null)
            {
                //session.Player.SendGenericError();
                // TODO: show error
                return;
            }

            if (!CanVisitResidence(session.Player, residence))
                return;

            IMapLock mapLock = mapLockManager.GetResidenceLock(residence.Parent ?? residence);

            // teleport player to correct residence instance
            IResidenceEntrance entrance = globalResidenceManager.GetResidenceEntrance(residence.PropertyInfoId);
            session.Player.Rotation = entrance.Rotation.ToEuler();
            session.Player.TeleportTo(new MapPosition
            {
                Info = new MapInfo
                {
                    Entry   = entrance.Entry,
                    MapLock = mapLock
                },
                Position = entrance.Position
            });
        }

        private bool CanVisitResidence(IPlayer player, IResidence residence)
        {
            switch (residence.Type)
            {
                case ResidenceType.Residence:
                    return residence.OwnerIdentity == player.Identity
                        || residence.PrivacyLevel == ResidencePrivacyLevel.Public;
                case ResidenceType.Community:
                {
                    if (residence.GuildOwnerIdentity == null)
                        return false;

                    IGuildBase guild = globalGuildManager.GetGuild(residence.GuildOwnerIdentity);
                    if (guild is not ICommunity community
                        || community.Identity != residence.GuildOwnerIdentity
                        || !ReferenceEquals(community.Residence, residence))
                        return false;

                    bool isPrivate = (community.Flags & GuildFlag.CommunityPrivate) != 0;
                    return !isPrivate || community.GetMember(player.CharacterId) != null;
                }
                default:
                    return false;
            }
        }
    }
}
