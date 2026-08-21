using NexusForever.Database;
using NexusForever.Database.Auth;

namespace NexusForever.Game.Abstract.Account.Costume
{
    public interface ICostumeUnlock : IDatabaseAuth, IDatabaseState
    {
        uint ItemId { get; }

        /// <summary>
        /// Stage the costume unlock change and invoke the supplied action when a requested deletion commits.
        /// </summary>
        void Save(AuthContext context, ISaveCommitScope commitScope, Action deleteAcknowledged);
    }
}
