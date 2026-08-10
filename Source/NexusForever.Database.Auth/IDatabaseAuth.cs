using NexusForever.Database;

namespace NexusForever.Database.Auth
{
    public interface IDatabaseAuth
    {
        void Save(AuthContext context);

        /// <summary>
        /// Stage authentication database changes and register acknowledgements for a successful commit.
        /// </summary>
        /// <param name="context">Authentication database context.</param>
        /// <param name="commitScope">Scope receiving post-commit acknowledgements.</param>
        void Save(AuthContext context, ISaveCommitScope commitScope)
        {
            Save(context);
        }
    }
}
