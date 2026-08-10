using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Entity
{
    public class StatValue : IStatValue
    {
        [Flags]
        public enum StatSaveMask
        {
            None   = 0x00,
            Create = 0x01,
            Value  = 0x02,
            Data   = 0x04
        }

        public Stat Stat { get; }
        public StatType Type { get; }

        public float Value
        {
            get => value;
            set
            {
                if (this.value == value)
                    return;

                this.value = value;
                saveMask.Mark(StatSaveMask.Value);
            }
        }

        private float value;

        public uint Data
        {
            get => data;
            set
            {
                if (data == value)
                    return;

                data = value;
                saveMask.Mark(StatSaveMask.Data);
            }
        }
        private uint data;

        private readonly VersionedSaveMask<StatSaveMask> saveMask = new();

        /// <summary>
        /// Create a new <see cref="IStatValue"/> from an existing database model.
        /// </summary>
        public StatValue(CharacterStatModel model)
        {
            Stat  = (Stat)model.Stat;
            Type  = EntityManager.Instance.GetStatAttribute(Stat).Type;
            value = model.Value;
            data  = model.Data;
        }

        /// <summary>
        /// Create a new <see cref="IStatValue"/> from an existing database model.
        /// </summary>
        public StatValue(EntityStatModel model)
        {
            Stat  = (Stat)model.Stat;
            Type  = EntityManager.Instance.GetStatAttribute(Stat).Type;
            value = model.Value;
        }

        /// <summary>
        /// Create a new <see cref="IStatValue"/> from supplied <see cref="Stat"/> and value.
        /// </summary>
        public StatValue(Stat stat, uint value)
        {
            Stat       = stat;
            Type       = StatType.Integer;
            this.value = value;
            saveMask.Mark(StatSaveMask.Create);
        }

        /// <summary>
        /// Create a new <see cref="IStatValue"/> from supplied <see cref="Stat"/> and value.
        /// </summary>
        public StatValue(Stat stat, float value)
        {
            Stat       = stat;
            Type       = StatType.Float;
            this.value = value;
            saveMask.Mark(StatSaveMask.Create);
        }

        public StatValue(Stat stat, uint value, uint data)
        {
            Stat       = stat;
            Type       = StatType.Data;
            this.value = value;
            this.data  = data;
            saveMask.Mark(StatSaveMask.Create);
        }

        public void SaveCharacter(ulong characterId, CharacterContext context)
        {
            SaveCharacter(characterId, context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage character stat changes and acknowledge them after the character database commits.
        /// </summary>
        public void SaveCharacter(ulong characterId, CharacterContext context, ISaveCommitScope commitScope)
        {
            VersionedSaveMaskSnapshot<StatSaveMask> snapshot = saveMask.Capture();
            StatSaveMask mask = snapshot.Mask;
            if (mask == StatSaveMask.None)
                return;

            if ((mask & StatSaveMask.Create) != 0)
            {
                context.Add(new CharacterStatModel
                {
                    Id    = characterId,
                    Stat  = (byte)Stat,
                    Value = Value,
                    Data  = Data
                });
            }
            else
            {
                var statModel = new CharacterStatModel
                {
                    Id   = characterId,
                    Stat = (byte)Stat
                };

                EntityEntry<CharacterStatModel> statEntity = context.Attach(statModel);
                if ((mask & StatSaveMask.Value) != 0)
                {
                    statModel.Value = Value;
                    statEntity.Property(p => p.Value).IsModified = true;
                }

                if ((mask & StatSaveMask.Data) != 0)
                {
                    statModel.Data = Data;
                    statEntity.Property(p => p.Data).IsModified = true;
                }
            }

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }

        /*public void SaveEntity(CharacterContext context)
        {
        }*/
    }
}
