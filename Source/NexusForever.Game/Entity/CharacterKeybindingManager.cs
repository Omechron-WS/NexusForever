using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Option;
using NexusForever.Game.Option;
using NexusForever.Game.Persistence;
using NexusForever.Network.World.Message.Model.Option;

namespace NexusForever.Game.Entity
{
    public class CharacterKeybindingManager : ICharacterKeybindingManager
    {
        private readonly IPlayer player;
        private readonly IKeybindingSet bindingSet;

        public CharacterKeybindingManager(IPlayer player, CharacterModel model)
        {
            this.player = player;
            bindingSet  = new KeybindingSet(model);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage character keybinding changes and acknowledge them after the character database commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
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
                CharacterId = player.CharacterId
            };
        }
    }
}
