using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Game.Static.Entity;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Shared;

namespace NexusForever.Game.Abstract.Entity
{
    public interface IDatacube : IDatabaseCharacter, INetworkBuildable<Datacube>
    {
        /// <summary>
        /// Stage datacube changes and register acknowledgements for a successful commit.
        /// </summary>
        new void Save(CharacterContext context, ISaveCommitScope commitScope);

        ushort Id { get; }
        DatacubeType Type { get; }
        uint Progress { get; set; }
    }
}
