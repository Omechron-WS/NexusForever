using NexusForever.Game;
using NexusForever.Game.Static.Group;
using NexusForever.Network;
using NexusForever.Network.Internal;
using NexusForever.Network.Internal.Message.Group;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Shared;
using NexusForever.WorldServer.Network.Internal;

namespace NexusForever.WorldServer.Network.Message.Handler.Group
{
    public class ClientGroupSetRoleHandler : IMessageHandler<IWorldSession, ClientGroupSetRole>
    {
        #region Dependency Injection

        private readonly IInternalMessagePublisher messagePublisher;

        public ClientGroupSetRoleHandler(
            IInternalMessagePublisher messagePublisher)
        {
            this.messagePublisher = messagePublisher;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientGroupSetRole groupSetRole)
        {
            if (!IsValidChange(groupSetRole.CurrentFlags, groupSetRole.ChangedFlag))
                throw new InvalidPacketValueException();

            messagePublisher.PublishAsync(new GroupMemberFlagUpdateMessage
            {
                GroupId      = groupSetRole.GroupId,
                Source       = session.Player.Identity.ToInternalIdentity(),
                Target       = groupSetRole.TargetedPlayer.ToInternalIdentity(),
                CurrentFlags = groupSetRole.CurrentFlags,
                ChangedFlag  = groupSetRole.ChangedFlag,
            }).FireAndForgetAsync();
        }

        private static bool IsValidChange(GroupMemberInfoFlags currentFlags, GroupMemberInfoFlags changedFlag)
        {
            const GroupMemberInfoFlags allDefinedFlags = GroupMemberInfoFlags.CanInvite
                | GroupMemberInfoFlags.CanKick
                | GroupMemberInfoFlags.Disconnected
                | GroupMemberInfoFlags.Pending
                | GroupMemberInfoFlags.RoleFlags
                | GroupMemberInfoFlags.MainTank
                | GroupMemberInfoFlags.MainAssist
                | GroupMemberInfoFlags.RaidAssistant
                | GroupMemberInfoFlags.Ready
                | GroupMemberInfoFlags.RoleLocked
                | GroupMemberInfoFlags.CanMark
                | GroupMemberInfoFlags.HasSetReady;

            if ((currentFlags & ~allDefinedFlags) != 0 || !IsValidChangedFlag(changedFlag))
                return false;

            bool allSet = (currentFlags & changedFlag) == changedFlag;
            bool allClear = (currentFlags & changedFlag) == 0;
            if (!allSet && !allClear)
                return false;

            return (changedFlag & GroupMemberInfoFlags.HasSetReady) == 0 || allSet;
        }

        private static bool IsValidChangedFlag(GroupMemberInfoFlags changedFlag)
        {
            return changedFlag == GroupMemberInfoFlags.None
                || changedFlag == GroupMemberInfoFlags.CanInvite
                || changedFlag == GroupMemberInfoFlags.CanKick
                || changedFlag == GroupMemberInfoFlags.Tank
                || changedFlag == GroupMemberInfoFlags.Healer
                || changedFlag == GroupMemberInfoFlags.DPS
                || changedFlag == GroupMemberInfoFlags.MainTank
                || changedFlag == GroupMemberInfoFlags.MainAssist
                || changedFlag == GroupMemberInfoFlags.RaidAssistant
                || changedFlag == GroupMemberInfoFlags.Ready
                || changedFlag == GroupMemberInfoFlags.RoleLocked
                || changedFlag == GroupMemberInfoFlags.CanMark
                || changedFlag == GroupMemberInfoFlags.HasSetReady
                || changedFlag == (GroupMemberInfoFlags.Ready | GroupMemberInfoFlags.HasSetReady);
        }
    }
}
