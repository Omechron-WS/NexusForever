using NexusForever.Database.Friendship;
using NexusForever.Game.Static.Friendship;
using NexusForever.Network.Internal.Message.Friendship;
using NexusForever.Server.Friendship.Game.Character;
using NexusForever.Server.Friendship.Game.Friend;
using Rebus.Handlers;

namespace NexusForever.Server.Friendship.Network.Internal.Handler.Friendship
{
    public class FriendshipRemoveIdentityHandler : IHandleMessages<FriendshipRemoveIdentityMessage>
    {
        #region Dependency Injection

        private readonly FriendshipContext _context;
        private readonly CharacterManager _characterManager;
        private readonly FriendManager _friendManager;

        public FriendshipRemoveIdentityHandler(
            FriendshipContext context,
            CharacterManager characterManager,
            FriendManager friendManager)
        {
            _context = context;
            _characterManager = characterManager;
            _friendManager = friendManager;
        }

        #endregion

        public async Task Handle(FriendshipRemoveIdentityMessage message)
        {
            Character inviterCharacter = await _characterManager.GetCharacterAsync(message.Inviter.ToFriendshipIdentity());
            if (inviterCharacter == null)
                return;

            Friend friend = await inviterCharacter.GetFriendByIdentityAsync(message.Invitee.ToFriendshipIdentity());
            if (friend == null)
                return;

            switch (friend.Type, message.Type)
            {
                case (FriendshipType.FriendAndRival, FriendshipType.Friend):
                    await friend.UpdateType(FriendshipType.Rival);
                    break;
                case (FriendshipType.FriendAndRival, FriendshipType.Rival):
                    await friend.UpdateType(FriendshipType.Friend);
                    break;
                case (FriendshipType.Friend, FriendshipType.Friend):
                case (FriendshipType.Ignore, FriendshipType.Ignore):
                case (FriendshipType.Rival, FriendshipType.Rival):
                    Character inviteeCharacter = await friend.GetInviteeCharacterAsync();
                    if (inviteeCharacter == null)
                        return;

                    await inviterCharacter.RemoveFriendAsync(friend);
                    inviteeCharacter.RemoveFriendInverse(friend);

                    _friendManager.RemoveFriend(friend);
                    break;
                default:
                    return;
            }

            await _context.SaveChangesAsync();
        }
    }
}
