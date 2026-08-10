using System.Collections;
using NexusForever.Database;
using NexusForever.Database.Auth;
using NexusForever.Database.Auth.Model;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Option;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Option;
using NexusForever.Network.World.Message.Model.Option;
using NetworkBinding = NexusForever.Network.World.Message.Model.Shared.Binding;

namespace NexusForever.Game.Option
{
    // TODO: split this further to seperate character and account keybind specific methods
    public class KeybindingSet : IKeybindingSet
    {
        [Flags]
        private enum KeybindingSetSaveMask
        {
            None     = 0x00,
            Bindings = 0x01
        }

        public ulong Owner { get; }
        public InputSets InputSet { get; }
        public uint Count => (uint)bindings.Count;

        private readonly VersionedSaveMask<KeybindingSetSaveMask> saveMask = new();

        private readonly Dictionary<ushort, IKeybinding> bindings = new();

        /// <summary>
        /// Create a new <see cref="KeybindingSet"/> from an existing database model.
        /// </summary>
        public KeybindingSet(CharacterModel model)
        {
            Owner    = model.Id;
            InputSet = InputSets.Character;

            foreach (CharacterKeybindingModel binding in model.Keybinding)
                bindings.Add(binding.InputActionId, new Keybinding(Owner, binding));
        }

        /// <summary>
        /// Create a new <see cref="KeybindingSet"/> from an existing database model.
        /// </summary>
        public KeybindingSet(AccountModel model)
        {
            Owner    = model.Id;
            InputSet = InputSets.Account;

            foreach (AccountKeybindingModel binding in model.AccountKeybinding)
                bindings.Add(binding.InputActionId, new Keybinding(Owner, binding));
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
            VersionedSaveMaskSnapshot<KeybindingSetSaveMask> snapshot = saveMask.Capture();
            if (snapshot.Mask == KeybindingSetSaveMask.None)
                return;

            List<IKeybinding> deletedBindings = bindings.Values
                .Where(binding => binding.PendingDelete)
                .ToList();

            foreach (IKeybinding binding in bindings.Values)
                binding.Save(context, commitScope);

            RegisterAcknowledgement(commitScope, snapshot, deletedBindings);
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
            VersionedSaveMaskSnapshot<KeybindingSetSaveMask> snapshot = saveMask.Capture();
            if (snapshot.Mask == KeybindingSetSaveMask.None)
                return;

            List<IKeybinding> deletedBindings = bindings.Values
                .Where(binding => binding.PendingDelete)
                .ToList();

            foreach (IKeybinding binding in bindings.Values)
                binding.Save(context, commitScope);

            RegisterAcknowledgement(commitScope, snapshot, deletedBindings);
        }

        private void RegisterAcknowledgement(
            ISaveCommitScope commitScope,
            VersionedSaveMaskSnapshot<KeybindingSetSaveMask> snapshot,
            IEnumerable<IKeybinding> deletedBindings)
        {
            commitScope.Register(() =>
            {
                foreach (IKeybinding deletedBinding in deletedBindings)
                    if (bindings.TryGetValue(deletedBinding.InputActionId, out IKeybinding currentBinding)
                        && ReferenceEquals(currentBinding, deletedBinding))
                        bindings.Remove(deletedBinding.InputActionId);

                saveMask.Acknowledge(snapshot);
            });
        }

        /// <summary>
        /// Update <see cref="KeybindingSet"/> with information from supplied <see cref="BiInputKeySet"/> from client.
        /// </summary>
        public void Update(BiInputKeySet biInputKeySet)
        {
            if (bindings.Count + biInputKeySet.Bindings.Count == 0)
                return;

            saveMask.Mark(KeybindingSetSaveMask.Bindings);

            foreach (ushort inputActionId in bindings.Keys
                .Except(biInputKeySet.Bindings.Select(b => b.InputActionId)))
            {
                IKeybinding binding = bindings[inputActionId];
                if (binding.PendingCreate)
                    bindings.Remove(inputActionId);
                else
                    binding.EnqueueDelete(true);
            }

            foreach (NetworkBinding networkBinding in biInputKeySet.Bindings)
            {
                if (!bindings.TryGetValue(networkBinding.InputActionId, out IKeybinding binding))
                    bindings.Add(networkBinding.InputActionId, new Keybinding(Owner, networkBinding));
                else
                {
                    if (binding.PendingDelete)
                        binding.EnqueueDelete(false);

                    binding.Update(networkBinding);
                }   
            }
        }

        public IEnumerator<IKeybinding> GetEnumerator()
        {
            return bindings.Values.Where(b => !b.PendingDelete).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
