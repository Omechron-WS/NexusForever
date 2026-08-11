using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NexusForever.Game.Group;
using NexusForever.Game.Static.Group;
using NexusForever.Network.Internal.Message.Group;
using NexusForever.Network.Internal.Message.Player;
using NLog;
using Rebus.Handlers;
using InternalGroup = NexusForever.Network.Internal.Message.Group.Shared.Group;
using InternalIdentity = NexusForever.Network.Internal.Message.Shared.Identity;

namespace NexusForever.WorldServer.Network.Internal.Handler.Group
{
    /// <summary>
    /// Maintains the world-local immutable group snapshot cache from authoritative bus messages.
    /// </summary>
    public sealed class GroupSnapshotHandler :
        IHandleMessages<GroupFlagsUpdatedMessage>,
        IHandleMessages<GroupLootRulesUpdatedMessage>,
        IHandleMessages<GroupMemberAddedMessage>,
        IHandleMessages<GroupMemberFlagsUpdatedMessage>,
        IHandleMessages<GroupMemberJoinedMessage>,
        IHandleMessages<GroupMemberLeftMessage>,
        IHandleMessages<GroupMemberPromotedMessage>,
        IHandleMessages<GroupMemberRemovedMessage>,
        IHandleMessages<GroupDisbandedMessage>,
        IHandleMessages<PlayerGroupAssociationUpdatedMessage>
    {
        private const int MaximumRejectedUpdateWarnings = 256;

        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly ConcurrentDictionary<(ulong GroupId, string MessageKind), byte> rejectedUpdateWarnings = [];
        private static int rejectedUpdateWarningCount;

        private readonly GroupSnapshotCache groupSnapshotCache;

        public GroupSnapshotHandler(GroupSnapshotCache groupSnapshotCache)
        {
            this.groupSnapshotCache = groupSnapshotCache;
        }

        public Task Handle(GroupFlagsUpdatedMessage message)
        {
            return Upsert(message?.Group, nameof(GroupFlagsUpdatedMessage));
        }

        public Task Handle(GroupLootRulesUpdatedMessage message)
        {
            return Upsert(message?.Group, nameof(GroupLootRulesUpdatedMessage));
        }

        public Task Handle(GroupMemberAddedMessage message)
        {
            return Upsert(message?.Group, nameof(GroupMemberAddedMessage));
        }

        public Task Handle(GroupMemberFlagsUpdatedMessage message)
        {
            return Upsert(message?.Group, nameof(GroupMemberFlagsUpdatedMessage));
        }

        public Task Handle(GroupMemberJoinedMessage message)
        {
            return Upsert(message?.Group, nameof(GroupMemberJoinedMessage));
        }

        public Task Handle(GroupMemberPromotedMessage message)
        {
            return Upsert(message?.Group, nameof(GroupMemberPromotedMessage));
        }

        public Task Handle(PlayerGroupAssociationUpdatedMessage message)
        {
            return Upsert(message?.Group, nameof(PlayerGroupAssociationUpdatedMessage), allowMissingGroup: true);
        }

        public Task Handle(GroupMemberRemovedMessage message)
        {
            if (message?.Group == null)
                return RejectMissingRemovalGroup(nameof(GroupMemberRemovedMessage));

            return ApplyRemoval(
                message.Group,
                message.RemovedMember?.Identity,
                message.Reason,
                nameof(GroupMemberRemovedMessage));
        }

        public Task Handle(GroupMemberLeftMessage message)
        {
            if (message?.Group == null)
                return RejectMissingRemovalGroup(nameof(GroupMemberLeftMessage));

            return ApplyRemoval(
                message.Group,
                message.RemovedMember?.Identity,
                message.Reason,
                nameof(GroupMemberLeftMessage));
        }

        public Task Handle(GroupDisbandedMessage message)
        {
            ulong groupId = message?.Group?.Id ?? 0ul;
            ulong revision = message?.Group?.Revision ?? 0ul;
            if (!groupSnapshotCache.MarkDisbanded(groupId, revision))
                WarnRejectedUpdate(groupId, nameof(GroupDisbandedMessage));

            return Task.CompletedTask;
        }

        private Task Upsert(InternalGroup group, string messageKind, bool allowMissingGroup = false)
        {
            if (group == null)
            {
                if (!allowMissingGroup)
                    WarnRejectedUpdate(0ul, messageKind);

                return Task.CompletedTask;
            }

            if (!groupSnapshotCache.TryUpsert(group))
                WarnRejectedUpdate(group.Id, messageKind);

            return Task.CompletedTask;
        }

        private Task ApplyRemoval(
            InternalGroup group,
            InternalIdentity removedIdentity,
            RemoveReason reason,
            string messageKind)
        {
            if (!Enum.IsDefined(reason))
            {
                groupSnapshotCache.Reject(group.Id, group.Revision);
                WarnRejectedUpdate(group.Id, messageKind);
                return Task.CompletedTask;
            }

            if (reason == RemoveReason.Disband)
            {
                if (!groupSnapshotCache.MarkDisbanded(group.Id, group.Revision))
                    WarnRejectedUpdate(group.Id, messageKind);

                return Task.CompletedTask;
            }

            if (!groupSnapshotCache.TryApplyMemberRemoval(group, removedIdentity))
                WarnRejectedUpdate(group.Id, messageKind);

            return Task.CompletedTask;
        }

        private static Task RejectMissingRemovalGroup(string messageKind)
        {
            WarnRejectedUpdate(0ul, messageKind);
            return Task.CompletedTask;
        }

        private static void WarnRejectedUpdate(ulong groupId, string messageKind)
        {
            var key = (groupId, messageKind);
            if (!rejectedUpdateWarnings.TryAdd(key, 0))
                return;

            int warningNumber = Interlocked.Increment(ref rejectedUpdateWarningCount);
            if (warningNumber > MaximumRejectedUpdateWarnings)
            {
                rejectedUpdateWarnings.TryRemove(key, out _);
                return;
            }

            log.Warn(
                "Rejected authoritative group snapshot message {0} for group {1}.",
                messageKind,
                groupId);
        }
    }
}
