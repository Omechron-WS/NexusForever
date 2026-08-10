using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Customisation;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Customisation;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Reputation;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.Game.Entity
{
    public class AppearanceManager : IAppearanceManager
    {
        private readonly Dictionary</*label*/uint, ICustomisation> characterCustomisations = new();
        private readonly Dictionary<ItemSlot, IAppearance> characterAppearances = new();
        private readonly Dictionary<byte, IBone> characterBones = new();

        private readonly IPlayer owner;

        /// <summary>
        /// Create a new <see cref="IAppearanceManager"/> for <see cref="IPlayer"/>.
        /// </summary>
        public AppearanceManager(IPlayer player, CharacterModel model)
        {
            owner = player;

            foreach (CharacterAppearanceModel characterAppearance in model.Appearance)
            {
                IAppearance appearance = new Appearance(characterAppearance);
                characterAppearances.Add((ItemSlot)characterAppearance.Slot, appearance);

                owner.AddVisual(appearance.ItemSlot, appearance.DisplayId);
            }

            foreach (CharacterCustomisationModel characterCustomisation in model.Customisation)
                characterCustomisations.Add(characterCustomisation.Label, new Customisation(characterCustomisation));

            foreach (CharacterBoneModel bone in model.Bone.OrderBy(bone => bone.BoneIndex))
                characterBones.Add(bone.BoneIndex, new Bone(bone));
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage appearance graph changes and register their successful-commit acknowledgements.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            foreach (IAppearance appearance in characterAppearances.Values.ToArray())
            {
                appearance.Save(context, commitScope, () =>
                {
                    if (characterAppearances.TryGetValue(appearance.ItemSlot, out IAppearance current) &&
                        ReferenceEquals(current, appearance))
                        characterAppearances.Remove(appearance.ItemSlot);
                });
            }

            foreach (IBone bone in characterBones.Values.ToArray())
            {
                bone.Save(context, commitScope, () =>
                {
                    if (characterBones.TryGetValue(bone.BoneIndex, out IBone current) && ReferenceEquals(current, bone))
                        characterBones.Remove(bone.BoneIndex);
                });
            }

            foreach (ICustomisation customisation in characterCustomisations.Values.ToArray())
            {
                customisation.Save(context, commitScope, () =>
                {
                    if (characterCustomisations.TryGetValue(customisation.Label, out ICustomisation current) &&
                        ReferenceEquals(current, customisation))
                        characterCustomisations.Remove(customisation.Label);
                });
            }
        }

        /// <summary>
        /// Return a collection of <see cref="ICustomisation"/> for <see cref="IPlayer"/>.
        /// </summary>
        public IEnumerable<ICustomisation> GetCustomisations()
        {
            return characterCustomisations.Values.Where(customisation => !customisation.PendingDelete);
        }

        /// <summary>
        /// Return a collection of <see cref="IAppearance"/> for <see cref="IPlayer"/>.
        /// </summary>
        public IEnumerable<IAppearance> GetAppearances()
        {
            return characterAppearances.Values.Where(appearance => !appearance.PendingDelete);
        }

        /// <summary>
        /// Return a collection of <see cref="IBone"/> for <see cref="IPlayer"/>.
        /// </summary>
        public IEnumerable<IBone> GetBones()
        {
            return characterBones.Values
                .Where(bone => !bone.PendingDelete)
                .OrderBy(bone => bone.BoneIndex);
        }

        /// <summary>
        /// Update <see cref="IPlayer"/> appearance.
        /// This will update, <see cref="Race"/>, <see cref="Sex"/>, customisations and bones.
        /// </summary>
        /// <remarks>
        /// This will do no validation, for customisation validation see <see cref="ICustomisationManager.Validate(Race, Sex, Faction, IList{ValueTuple{uint, uint}})"/>.
        /// </remarks>
        public void Update(Race race, Sex sex, IList<(uint Label, uint Value)> customisations, IList<float> bones)
        {
            owner.Race = race;
            owner.Sex  = sex;

            UpdateCustomisations(customisations);
            UpdateAppearances(race, sex, customisations);
            UpdateBones(bones);
        }

        private void UpdateCustomisations(IList<(uint Label, uint Value)> customisations)
        {
            foreach ((uint label, uint value) in customisations)
            {
                if (characterCustomisations.TryGetValue(label, out ICustomisation customisation))
                {
                    if (customisation.PendingDelete)
                        customisation.EnqueueDelete(false);
                    customisation.Value = value;
                }
                else
                    characterCustomisations.TryAdd(label, new Customisation(owner.CharacterId, label, value));
            }

            HashSet<uint> retainedLabels = customisations
                .Select(customisation => customisation.Label)
                .ToHashSet();
            foreach (ICustomisation customisation in characterCustomisations.Values
                .Where(customisation => !retainedLabels.Contains(customisation.Label))
                .ToArray())
            {
                if (!customisation.PendingDelete)
                    customisation.Delete();
            }
        }

        private void UpdateAppearances(Race race, Sex sex, IList<(uint Label, uint Value)> customisations)
        {
            List<IItemVisual> itemVisuals = CustomisationManager.Instance.GetItemVisuals(race, sex, customisations).ToList();
            foreach (IItemVisual visual in itemVisuals)
            {
                if (characterAppearances.TryGetValue(visual.Slot, out IAppearance appearance))
                {
                    if (appearance.PendingDelete)
                        appearance.EnqueueDelete(false);
                    appearance.DisplayId = visual.DisplayId.Value;
                }
                else
                    characterAppearances.TryAdd(visual.Slot, new Appearance(owner.CharacterId, visual.Slot, visual.DisplayId.Value));

                owner.AddVisual(visual);
            }

            HashSet<ItemSlot> retainedSlots = itemVisuals
                .Select(visual => visual.Slot)
                .ToHashSet();
            foreach (IAppearance appearance in characterAppearances.Values
                .Where(appearance => !retainedSlots.Contains(appearance.ItemSlot))
                .ToArray())
            {
                if (!appearance.PendingDelete)
                    appearance.Delete();
            }
        }

        private void UpdateBones(IList<float> bones)
        {
            if (bones.Count > byte.MaxValue + 1)
                throw new ArgumentOutOfRangeException(nameof(bones));

            for (int i = 0; i < bones.Count; i++)
            {
                byte boneIndex = (byte)i;
                if (characterBones.TryGetValue(boneIndex, out IBone bone))
                {
                    if (bone.PendingDelete)
                        bone.EnqueueDelete(false);
                    bone.BoneValue = bones[i];
                }
                else
                    characterBones.Add(boneIndex, new Bone(owner.CharacterId, boneIndex, bones[i]));
            }

            foreach (IBone bone in characterBones.Values
                .Where(bone => bone.BoneIndex >= bones.Count)
                .ToArray())
            {
                if (!bone.PendingDelete)
                    bone.Delete();
            }

            owner.EnqueueToVisible(new ServerEntityBoneUpdate
            {
                UnitId = owner.Guid,
                Bones  = GetBones()
                    .Select(bone => bone.BoneValue)
                    .ToList()
            }, true);
        }
    }
}
