using NexusForever.Database;
using NexusForever.Database.Auth;
using NexusForever.Database.Auth.Model;
using NexusForever.Game.Abstract.RBAC;
using NexusForever.Game.Persistence;

namespace NexusForever.Game.RBAC
{
    public class AccountPermission : IAccountPermission
    {
        [Flags]
        private enum SaveMask
        {
            None   = 0x00,
            Create = 0x01,
            Delete = 0x02
        }

        public uint Id { get; }
        public IRBACPermission Permission { get; }

        /// <summary>
        /// Returns if <see cref="IAccountPermission"/> is enqueued to be saved to the database.
        /// </summary>
        public bool PendingCreate => (saveMask.Current & SaveMask.Create) != 0;

        /// <summary>
        /// Returns if <see cref="IAccountPermission"/> is enqueued to be deleted from the database.
        /// </summary>
        public bool PendingDelete => (saveMask.Current & SaveMask.Delete) != 0;

        private readonly VersionedSaveMask<SaveMask> saveMask = new();

        /// <summary>
        /// Create a new <see cref="IAccountPermission"/> from an existing database model.
        /// </summary>
        public AccountPermission(AccountPermissionModel model, IRBACPermission permission)
        {
            Id         = model.Id;
            Permission = permission;
        }

        /// <summary>
        /// Create a new <see cref="IAccountPermission"/> from a <see cref="IRBACPermission"/>.
        /// </summary>
        public AccountPermission(uint id, IRBACPermission permission)
        {
            Id         = id;
            Permission = permission;
            saveMask.Mark(SaveMask.Create);
        }

        public void Save(AuthContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage the account permission change and acknowledge it after the authentication database commits.
        /// </summary>
        public void Save(AuthContext context, ISaveCommitScope commitScope)
        {
            VersionedSaveMaskSnapshot<SaveMask> snapshot = saveMask.Capture();
            SaveMask mask = snapshot.Mask;
            if (mask == SaveMask.None)
                return;

            var model = new AccountPermissionModel
            {
                Id           = Id,
                PermissionId = (uint)Permission.Permission
            };

            if ((mask & SaveMask.Create) != 0)
                context.Add(model);
            else
                context.Remove(model);

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }

        /// <summary>
        /// Enqueue <see cref="IAccountPermission"/> to be deleted from the database.
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
