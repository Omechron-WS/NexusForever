using NexusForever.Game;
using NexusForever.Game.Static.Friendship;
using NexusForever.Network;
using NexusForever.Network.Internal;
using NexusForever.Network.Internal.Message.Friendship;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Friendship;
using NexusForever.Shared;

namespace NexusForever.WorldServer.Network.Message.Handler.Friendship
{
    public class ClientFriendshipAddByNameHandler : IMessageHandler<IWorldSession, ClientFriendshipAddByName>
    {
        #region Dependency Injection

        private readonly IInternalMessagePublisher messagePublisher;

        public ClientFriendshipAddByNameHandler(
            IInternalMessagePublisher messagePublisher)
        {
            this.messagePublisher = messagePublisher;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientFriendshipAddByName message)
        {
            if (message.Type is not (FriendshipType.Friend
                or FriendshipType.Ignore
                or FriendshipType.Rival
                or FriendshipType.Account))
                throw new InvalidPacketValueException();

            messagePublisher.PublishAsync(new FriendshipNameInviteRequestMessage
            {
                Inviter     = session.Player.Identity.ToInternalIdentity(),
                InviteeName = new NexusForever.Network.Internal.Message.Shared.IdentityName
                {
                    Name      = message.Name,
                    RealmName = message.RealmName
                },
                Type = message.Type,
                Note = message.Note != string.Empty ? message.Note : null
            }).FireAndForgetAsync();
        }
    }
}
