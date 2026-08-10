using NexusForever.Database;
using NexusForever.Database.Auth;
using NexusForever.Database.Auth.Model;
using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Unlock;
using NexusForever.Game.Account.Costume;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Account.Unlock
{
    public class GenericUnlock : IGenericUnlock
    {
        [Flags]
        private enum GenericUnlockSaveMask
        {
            None   = 0x00,
            Create = 0x01
        }

        public GenericUnlockEntryEntry Entry { get; }
        public GenericUnlockType Type => (GenericUnlockType)Entry.GenericUnlockTypeEnum;

        private readonly IAccount account;
        private readonly VersionedSaveMask<GenericUnlockSaveMask> saveMask = new();

        /// <summary>
        /// Create a new <see cref="IGenericUnlock"/> from existing <see cref="AccountGenericUnlockModel"/> database model.
        /// </summary>
        public GenericUnlock(IAccount account, AccountGenericUnlockModel model)
        {
            this.account = account;
            Entry        = GameTableManager.Instance.GenericUnlockEntry.GetEntry(model.Entry);
        }

        /// <summary>
        /// Create a new <see cref="IGenericUnlock"/> from supplied <see cref="GenericUnlockEntryEntry"/>.
        /// </summary>
        public GenericUnlock(IAccount account, GenericUnlockEntryEntry entry)
        {
            this.account = account;
            Entry        = entry;
            saveMask.Mark(GenericUnlockSaveMask.Create);
        }

        public void Save(AuthContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage the generic unlock and acknowledge it after the authentication database commits.
        /// </summary>
        public void Save(AuthContext context, ISaveCommitScope commitScope)
        {
            VersionedSaveMaskSnapshot<GenericUnlockSaveMask> snapshot = saveMask.Capture();
            if (snapshot.Mask == GenericUnlockSaveMask.None)
                return;

            var model = new AccountGenericUnlockModel
            {
                Id    = account.Id,
                Entry = Entry.Id
            };

            context.Add(model);
            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }
    }
}
