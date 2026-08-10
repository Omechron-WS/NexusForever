using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Entity;
using NetworkDatacube = NexusForever.Network.World.Message.Model.Shared.Datacube;

namespace NexusForever.Game.Entity
{
    public class Datacube : IDatacube
    {
        [Flags]
        public enum DatacubeSaveMask
        {
            None     = 0x0000,
            Create   = 0x0001,
            Progress = 0x0002
        }

        public ushort Id { get; }
        public DatacubeType Type { get; }

        public uint Progress
        {
            get => progress;
            set
            {
                if (progress == value)
                    return;

                progress = value;
                saveMask.Mark(DatacubeSaveMask.Progress);
            }
        }

        private uint progress;

        private readonly VersionedSaveMask<DatacubeSaveMask> saveMask = new();

        private readonly IPlayer player;

        /// <summary>
        /// Create a new <see cref="IDatacube"/> from the supplied id, <see cref="DatacubeType"/> and progress.
        /// </summary>
        public Datacube(IPlayer player, ushort id, DatacubeType type, uint progress)
        {
            this.player = player;

            Id       = id;
            Type     = type;
            this.progress = progress;
            saveMask.Mark(DatacubeSaveMask.Create);
        }

        /// <summary>
        /// Create a new <see cref="IDatacube"/> from an existing database model.
        /// </summary>
        public Datacube(IPlayer player, CharacterDatacubeModel model)
        {
            this.player = player;

            Id       = model.Datacube;
            Type     = (DatacubeType)model.Type;
            progress = model.Progress;
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage datacube changes and acknowledge them after the character database commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            VersionedSaveMaskSnapshot<DatacubeSaveMask> snapshot = saveMask.Capture();
            DatacubeSaveMask mask = snapshot.Mask;
            if (mask == DatacubeSaveMask.None)
                return;

            var model = new CharacterDatacubeModel
            {
                Id       = player.CharacterId,
                Type     = (byte)Type,
                Datacube = Id,
                Progress = Progress
            };

            if ((mask & DatacubeSaveMask.Create) != 0)
                context.Add(model);
            else if ((mask & DatacubeSaveMask.Progress) != 0)
            {
                EntityEntry<CharacterDatacubeModel> entity = context.Attach(model);
                entity.Property(p => p.Progress).IsModified = true;
            }

            commitScope.Register(() => saveMask.Acknowledge(snapshot));
        }

        public NetworkDatacube Build()
        {
            return new NetworkDatacube
            {
                DatacubeId = Id,
                Progress   = Progress
            };
        }
    }
}
