namespace NexusForever.Game.Quest
{
    /// <summary>
    /// Immutable server-side identity and expiry for a pending quest share.
    /// </summary>
    internal sealed class PendingQuestShare
    {
        public ushort QuestId { get; }
        public uint SharerGuid { get; }
        public ulong SharerCharacterId { get; }
        public ulong GroupAssociation { get; }
        public double ExpiresAt { get; }

        public PendingQuestShare(
            ushort questId,
            uint sharerGuid,
            ulong sharerCharacterId,
            ulong groupAssociation,
            double expiresAt)
        {
            QuestId            = questId;
            SharerGuid          = sharerGuid;
            SharerCharacterId  = sharerCharacterId;
            GroupAssociation   = groupAssociation;
            ExpiresAt          = expiresAt;
        }
    }
}
