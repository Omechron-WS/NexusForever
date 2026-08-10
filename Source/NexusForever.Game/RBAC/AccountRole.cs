using NexusForever.Database;
using NexusForever.Database.Auth;
using NexusForever.Database.Auth.Model;
using NexusForever.Game.Abstract.RBAC;
using NexusForever.Game.Persistence;

namespace NexusForever.Game.RBAC
{
    public class AccountRole : IAccountRole
    {
        [Flags]
        private enum SaveMask
        {
            None   = 0x00,
            Create = 0x01,
            Delete = 0x02
        }

        public uint Id { get; }
        public IRBACRole Role { get; }

        /// <summary>
        /// Returns if <see cref="IAccountRole"/> is enqueued to be saved to the database.
        /// </summary>
        public bool PendingCreate => (saveMask.Current & SaveMask.Create) != 0;

        /// <summary>
        /// Returns if <see cref="IAccountRole"/> is enqueued to be deleted from the database.
        /// </summary>
        public bool PendingDelete => (saveMask.Current & SaveMask.Delete) != 0;

        private readonly VersionedSaveMask<SaveMask> saveMask = new();

        /// <summary>
        /// Create a new <see cref="IAccountRole"/> from an existing database model.
        /// </summary>
        public AccountRole(AccountRoleModel model, IRBACRole role)
        {
            Id       = model.Id;
            Role     = role;
        }

        /// <summary>
        /// Create a new <see cref="IAccountRole"/> from a <see cref="IRBACRole"/>.
        /// </summary>
        public AccountRole(uint id, IRBACRole role)
        {
            Id       = id;
            Role     = role;
            saveMask.Mark(SaveMask.Create);
        }

        public void Save(AuthContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage the account role change and acknowledge it after the authentication database commits.
        /// </summary>
        public void Save(AuthContext context, ISaveCommitScope commitScope)
        {
            VersionedSaveMaskSnapshot<SaveMask> snapshot = saveMask.Capture();
            SaveMask mask = snapshot.Mask;
            if (mask == SaveMask.None)
                return;

            var model = new AccountRoleModel
            {
                Id     = Id,
                RoleId = (uint)Role.Role
            };

            if ((mask & SaveMask.Create) != 0)
                context.Add(model);
            else
                context.Remove(model);

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }

        /// <summary>
        /// Enqueue <see cref="IAccountRole"/> to be deleted from the database.
        /// </summary>
        public void EnqueueDelete(bool delete)
        {
            if (delete)
                saveMask.Mark(SaveMask.Delete);
            else
                saveMask.Clear(SaveMask.Delete);
        }
    }
}
