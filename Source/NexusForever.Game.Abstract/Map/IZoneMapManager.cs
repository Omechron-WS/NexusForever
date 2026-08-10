using System.Numerics;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Game.Abstract.Map
{
    public interface IZoneMapManager : IDatabaseCharacter
    {
        /// <summary>
        /// Stage zone-map discoveries and register acknowledgements for a successful commit.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        void SendInitialPackets();
        void SendZoneMaps();

        /// <summary>
        /// Invoked when <see cref="IPlayer"/> moves to a new <see cref="Vector3"/>.
        /// </summary>
        void OnRelocate(Vector3 vector);

        /// <summary>
        /// Invoked when <see cref="IPlayer"/> moves to a new zone.
        /// </summary>
        void OnZoneUpdate();
    }
}
