using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Costume;
using NexusForever.Game.Static.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model.Costume;

namespace NexusForever.Game.Entity
{
    public class CostumeItem : ICostumeItem
    {
        /// <summary>
        /// Determines which fields need saving for <see cref="ICostumeItem"/> when being saved to the database.
        /// </summary>
        [Flags]
        public enum CostumeItemSaveMask
        {
            None    = 0x00,
            Create  = 0x01,
            ItemId  = 0x02,
            DyeData = 0x04
        }

        public const byte MaxCostumeItemDyes = 3;

        /// <summary>
        /// Returns packed dye RampIndexes generated from supplied dyeColorRampIds.
        /// </summary>
        public static uint GenerateDyeData(uint[] dyeColorRampIds)
        {
            uint[] ramps = new uint[MaxCostumeItemDyes];
            for (var i = 0; i < dyeColorRampIds.Length; i++)
            {
                if (dyeColorRampIds[i] == 0)
                    continue;

                DyeColorRampEntry entry = GameTableManager.Instance.DyeColorRamp.GetEntry(dyeColorRampIds[i]);
                ramps[i] = (uint)entry.RampIndex;
            }

            return (uint)((ramps[2] & 0x3FF | 0xFFFFF800) << 20) | (ramps[1] & 0x3FF) << 10 | ramps[0] & 0x3FF;
        }

        public CostumeItemSlot Slot { get; }
        public ItemSlot ItemSlot { get; }
        public IItemInfo ItemInfo { get; private set; }

        public uint? Item2Id
        {
            get => ItemInfo?.Id;
            set
            {
                if (ItemInfo?.Id == value)
                    return;

                ItemInfo  = value.HasValue ? ItemManager.Instance.GetItemInfo(value.Value) : null;
                saveMask.Mark(CostumeItemSaveMask.ItemId);
            }
        }

        public ushort? DisplayId => ItemInfo?.GetDisplayId();

        public uint DyeData
        {
            get => dyeData;
            set
            {
                if (dyeData == value)
                    return;

                dyeData = value;
                saveMask.Mark(CostumeItemSaveMask.DyeData);
            }
        }

        private uint dyeData;

        private readonly ICostume costume;

        private VersionedSaveMask<CostumeItemSaveMask> saveMask = new();

        /// <summary>
        /// Create a new <see cref="ICostumeItem"/> from an existing <see cref="CharacterCostumeItemModel"/> database model.
        /// </summary>
        public CostumeItem(ICostume costume, CharacterCostumeItemModel model)
        {
            this.costume = costume;
            Slot         = (CostumeItemSlot)model.Slot;
            ItemSlot     = GetSlot(Slot);
            ItemInfo     = model.Item2Id > 0 ? ItemManager.Instance.GetItemInfo(model.Item2Id) : null;
            dyeData      = model.DyeData;

            saveMask     = new VersionedSaveMask<CostumeItemSaveMask>();
        }

        /// <summary>
        /// Create a new <see cref="ICostumeItem"/> from packet <see cref="ClientCostumeSave"/>.
        /// </summary>
        public CostumeItem(ICostume costume, ClientCostumeSave.CostumeItem item, CostumeItemSlot slot)
        {
            this.costume = costume;
            Slot         = slot;
            ItemSlot     = GetSlot(Slot);
            ItemInfo     = item.Item2Id > 0 ? ItemManager.Instance.GetItemInfo(item.Item2Id) : null;
            dyeData      = GenerateDyeData(item.DyeColorRampIds);

            saveMask     = new VersionedSaveMask<CostumeItemSaveMask>(CostumeItemSaveMask.Create);
        }

        private static ItemSlot GetSlot(CostumeItemSlot slot)
        {
            return slot switch
            {
                CostumeItemSlot.Chest    => ItemSlot.ArmorChest,
                CostumeItemSlot.Legs     => ItemSlot.ArmorLegs,
                CostumeItemSlot.Head     => ItemSlot.ArmorHead,
                CostumeItemSlot.Shoulder => ItemSlot.ArmorShoulder,
                CostumeItemSlot.Feet     => ItemSlot.ArmorFeet,
                CostumeItemSlot.Hands    => ItemSlot.ArmorHands,
                CostumeItemSlot.Weapon   => ItemSlot.WeaponPrimary,
                _                        => throw new ArgumentOutOfRangeException(nameof(slot))
            };
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage costume item changes and register their successful-commit acknowledgements.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(commitScope);

            VersionedSaveMaskSnapshot<CostumeItemSaveMask> snapshot = saveMask.Capture();
            CostumeItemSaveMask mask = snapshot.Mask;
            if (mask == CostumeItemSaveMask.None)
                return;

            if ((mask & CostumeItemSaveMask.Create) != 0)
            {
                // costume item doesn't exist in database, all infomation must be saved
                context.Add(new CharacterCostumeItemModel
                {
                    Id      = costume.Owner,
                    Index   = costume.Index,
                    Slot    = (byte)Slot,
                    Item2Id = Item2Id ?? 0,
                    DyeData = dyeData
                });
            }
            else
            {
                // costume item already exists in database, save only data that has been modified
                var model = new CharacterCostumeItemModel
                {
                    Id    = costume.Owner,
                    Index = costume.Index,
                    Slot  = (byte)Slot
                };

                EntityEntry<CharacterCostumeItemModel> entity = context.Attach(model);
                if ((mask & CostumeItemSaveMask.ItemId) != 0)
                {
                    model.Item2Id = Item2Id ?? 0;
                    entity.Property(p => p.Item2Id).IsModified = true;
                }
                if ((mask & CostumeItemSaveMask.DyeData) != 0)
                {
                    model.DyeData = dyeData;
                    entity.Property(p => p.DyeData).IsModified = true;
                }
            }

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }

        /// <summary>
        /// Get <see cref="IItemVisual"/> for <see cref="ICostumeItem"/>.
        /// </summary>
        public IItemVisual GetItemVisual()
        {
            return new ItemVisual
            {
                Slot      = ItemSlot,
                DisplayId = DisplayId,
                DyeData   = DyeData
            };
        }
    }
}
