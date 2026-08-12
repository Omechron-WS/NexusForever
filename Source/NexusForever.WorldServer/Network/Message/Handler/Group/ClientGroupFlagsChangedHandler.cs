using NexusForever.Game;
using NexusForever.Game.Static.Group;
using NexusForever.Network;
using NexusForever.Network.Internal;
using NexusForever.Network.Internal.Message.Group;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Group
{
    public class ClientGroupFlagsChangedHandler : IMessageHandler<IWorldSession, ClientGroupFlagsChanged>
    {
        private const GroupFlags SupportedGroupFlags = GroupFlags.OpenWorld
            | GroupFlags.Raid
            | GroupFlags.JoinRequestOpen
            | GroupFlags.JoinRequestClosed
            | GroupFlags.ReferralsOpen
            | GroupFlags.ReferralsClosed;

        #region Dependency Injection

        private readonly IInternalMessagePublisher messagePublisher;

        public ClientGroupFlagsChangedHandler(
            IInternalMessagePublisher messagePublisher)
        {
            this.messagePublisher = messagePublisher;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientGroupFlagsChanged groupFlagsChanged)
        {
            if (!IsValid(groupFlagsChanged.NewFlags))
                throw new InvalidPacketValueException();

            messagePublisher.PublishAsync(new GroupFlagsUpdateMessage
            {
                GroupId  = groupFlagsChanged.GroupId,
                Identity = session.Player.Identity.ToInternalIdentity(),
                Flags    = groupFlagsChanged.NewFlags
            });
        }

        private static bool IsValid(GroupFlags flags)
        {
            const GroupFlags joinRequestFlags = GroupFlags.JoinRequestOpen | GroupFlags.JoinRequestClosed;
            const GroupFlags referralFlags = GroupFlags.ReferralsOpen | GroupFlags.ReferralsClosed;

            return (flags & ~SupportedGroupFlags) == 0
                && (flags & joinRequestFlags) != joinRequestFlags
                && (flags & referralFlags) != referralFlags;
        }
    }
}
