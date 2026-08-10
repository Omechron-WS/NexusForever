using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Auth;
using NexusForever.Database.Auth.Model;
using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Entitlement;
using NexusForever.Game.Persistence;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.AccountInventory;

namespace NexusForever.Game.Account.Entitlement
{
    public sealed class AccountEntitlement : Game.Entitlement.Entitlement, IAccountEntitlement
    {
        private readonly IAccount account;

        /// <summary>
        /// Create a new <see cref="IAccountEntitlement"/> from an existing database model.
        /// </summary>
        public AccountEntitlement(IAccount account, AccountEntitlementModel model, EntitlementEntry entry)
            : base(entry, model.Amount)
        {
            this.account = account;
        }

        /// <summary>
        /// Create a new <see cref="IAccountEntitlement"/> from supplied <see cref="EntitlementEntry"/> and value.
        /// </summary>
        public AccountEntitlement(IAccount account, EntitlementEntry entry, uint value)
            : base(entry, value)
        {
            this.account = account;
            saveMask.Mark(SaveMask.Create);
        }

        public void Save(AuthContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage the account entitlement change and acknowledge it after the authentication database commits.
        /// </summary>
        public void Save(AuthContext context, ISaveCommitScope commitScope)
        {
            VersionedSaveMaskSnapshot<SaveMask> snapshot = saveMask.Capture();
            SaveMask mask = snapshot.Mask;
            if (mask == SaveMask.None)
                return;

            var model = new AccountEntitlementModel
            {
                Id            = account.Id,
                EntitlementId = (byte)Type,
                Amount        = amount
            };

            if ((mask & SaveMask.Create) != 0)
                context.Add(model);
            else
            {
                EntityEntry<AccountEntitlementModel> entity = context.Attach(model);
                entity.Property(p => p.Amount).IsModified = true;
            }

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }

        ServerAccountEntitlement INetworkBuildable<ServerAccountEntitlement>.Build()
        {
            return new ServerAccountEntitlement
            {
                EntitlementId = Type,
                Count       = Amount
            };
        }
    }
}
