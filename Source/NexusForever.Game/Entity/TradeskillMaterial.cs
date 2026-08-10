using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Persistence;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Entity
{
    public class TradeskillMaterial : ITradeskillMaterial
    {
        /// <summary>
        /// Determines which fields need saving for <see cref="ITradeskillMaterial"/> when being saved to the database.
        /// </summary>
        [Flags]
        public enum TradeskillMaterialSaveMask
        {
            None   = 0x0000,
            Create = 0x0001,
            Amount = 0x0002
        }

        public TradeskillMaterialEntry Entry { get; }
        public ulong Owner { get; }
        public ushort MaterialId { get; }

        public ushort Amount
        {
            get => amount;
            set
            {
                if (amount == value)
                    return;

                amount = value;
                saveMask.Mark(TradeskillMaterialSaveMask.Amount);
            }
        }
        private ushort amount;

        private readonly VersionedSaveMask<TradeskillMaterialSaveMask> saveMask = new();

        public TradeskillMaterial(CharacterTradeskillMaterialModel model)
        {
            Owner      = model.Id;
            MaterialId = model.MaterialId;
            amount     = model.Amount;

            Entry      = GameTableManager.Instance.TradeskillMaterial.GetEntry(MaterialId);

        }

        public TradeskillMaterial(ulong characterId, ushort materialId)
        {
            Owner      = characterId;
            MaterialId = materialId;
            amount     = 0;

            Entry      = GameTableManager.Instance.TradeskillMaterial.GetEntry(MaterialId);

            saveMask.Mark(TradeskillMaterialSaveMask.Create);
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage material changes and acknowledge them after the character database commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            VersionedSaveMaskSnapshot<TradeskillMaterialSaveMask> snapshot = saveMask.Capture();
            TradeskillMaterialSaveMask mask = snapshot.Mask;
            if (mask == TradeskillMaterialSaveMask.None)
                return;

            if ((mask & TradeskillMaterialSaveMask.Create) != 0)
            {
                var model = new CharacterTradeskillMaterialModel
                {
                    Id         = Owner,
                    MaterialId = MaterialId,
                    Amount     = amount
                };

                context.Add(model);
            }
            else
            {
                var model = new CharacterTradeskillMaterialModel
                {
                    Id         = Owner,
                    MaterialId = MaterialId
                };

                EntityEntry<CharacterTradeskillMaterialModel> entity = context.Attach(model);
                if ((mask & TradeskillMaterialSaveMask.Amount) != 0)
                {
                    model.Amount = amount;
                    entity.Property(p => p.Amount).IsModified = true;
                }
            }

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }
    }
}
