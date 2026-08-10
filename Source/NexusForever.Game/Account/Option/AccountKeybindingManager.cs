using NexusForever.Database;
using NexusForever.Database.Auth;
using NexusForever.Database.Auth.Model;
using NexusForever.Game.Abstract.Account.Option;
using NexusForever.Game.Abstract.Option;
using NexusForever.Game.Option;
using NexusForever.Game.Persistence;
using NexusForever.Network.World.Message.Model.Option;

namespace NexusForever.Game.Account.Option
{
    public class AccountKeybindingManager : IAccountKeybindingManager
    {
        private readonly IKeybindingSet bindingSet;

        public AccountKeybindingManager(AccountModel model)
        {
            bindingSet = new KeybindingSet(model);
        }

        public void Save(AuthContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage account keybinding changes and acknowledge them after the authentication database commits.
        /// </summary>
        public void Save(AuthContext context, ISaveCommitScope commitScope)
        {
            bindingSet.Save(context, commitScope);
        }

        public void Update(BiInputKeySet inputKeySet)
        {
            bindingSet.Update(inputKeySet);
        }

        public BiInputKeySet Build()
        {
            return new BiInputKeySet
            {
                Bindings    = bindingSet.Select(b => b.Build()).ToList(),
                CharacterId = 0ul
            };
        }
    }
}
