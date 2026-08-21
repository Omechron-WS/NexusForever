using Microsoft.EntityFrameworkCore;
using NexusForever.Database;
using NexusForever.Database.Auth;
using NexusForever.Database.Auth.Model;
using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Costume;
using NexusForever.Game.Persistence;

namespace NexusForever.Game.Account.Costume
{
    public class CostumeUnlock : ICostumeUnlock
    {
        [Flags]
        public enum CostumeUnlockSaveMask
        {
            None   = 0x00,
            Create = 0x01,
            Delete = 0x02
        }

        public uint ItemId { get; }

        private readonly uint accountId;
        private readonly VersionedSaveMask<CostumeUnlockSaveMask> saveMask = new();

        public bool PendingCreate => (saveMask.Current & CostumeUnlockSaveMask.Create) != 0;
        public bool PendingDelete => (saveMask.Current & CostumeUnlockSaveMask.Delete) != 0;

        /// <summary>
        /// Create a new <see cref="ICostumeUnlock"/> from an existing <see cref="AccountCostumeUnlockModel"/> database model.
        /// </summary>
        public CostumeUnlock(AccountCostumeUnlockModel model)
        {
            ItemId    = model.ItemId;
            accountId = model.Id;
        }

        /// <summary>
        /// Create a new <see cref="ICostumeUnlock"/> from supplied item id.
        /// </summary>
        public CostumeUnlock(IAccount account, uint itemId)
        {
            ItemId    = itemId;
            accountId = account.Id;
            saveMask.Mark(CostumeUnlockSaveMask.Create);
        }

        public void Save(AuthContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage the costume unlock change and acknowledge it after the authentication database commits.
        /// </summary>
        public void Save(AuthContext context, ISaveCommitScope commitScope)
        {
            Save(context, commitScope, null);
        }

        /// <summary>
        /// Stage the costume unlock change and invoke the supplied action when a requested deletion commits.
        /// </summary>
        public void Save(AuthContext context, ISaveCommitScope commitScope, Action deleteAcknowledged)
        {
            VersionedSaveMaskSnapshot<CostumeUnlockSaveMask> snapshot = saveMask.Capture();
            CostumeUnlockSaveMask mask = snapshot.Mask;
            if (mask == CostumeUnlockSaveMask.None)
                return;

            void Acknowledge()
            {
                bool deleteStillRequested = PendingDelete;
                saveMask.Acknowledge(snapshot);

                if ((mask & CostumeUnlockSaveMask.Delete) == 0)
                    return;

                if (deleteStillRequested)
                    deleteAcknowledged?.Invoke();
                else
                    saveMask.Mark(CostumeUnlockSaveMask.Create);
            }

            if ((mask & (CostumeUnlockSaveMask.Create | CostumeUnlockSaveMask.Delete)) ==
                (CostumeUnlockSaveMask.Create | CostumeUnlockSaveMask.Delete))
            {
                commitScope.Register(Acknowledge);
                return;
            }

            var model = new AccountCostumeUnlockModel
            {
                Id     = accountId,
                ItemId = ItemId
            };

            if ((mask & CostumeUnlockSaveMask.Create) != 0)
                context.Add(model);
            else
                context.Entry(model).State = EntityState.Deleted;

            commitScope.Register(Acknowledge);
        }

        /// <summary>
        /// Enqueue <see cref="ICostumeUnlock"/> to be deleted from the database.
        /// </summary>
        public void EnqueueDelete(bool set)
        {
            if (set)
                saveMask.Mark(CostumeUnlockSaveMask.Delete);
            else
                saveMask.Clear(CostumeUnlockSaveMask.Delete);
        }
    }
}
