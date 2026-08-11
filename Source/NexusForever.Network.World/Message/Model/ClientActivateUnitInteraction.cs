using NexusForever.Network.Message;

namespace NexusForever.Network.World.Message.Model
{
    /// <summary>
    /// Requests the deferred client-side interaction activation path for a visible entity.
    /// </summary>
    [Message(GameMessageOpcode.ClientActivateUnitInteraction)]
    public class ClientActivateUnitInteraction : IReadable
    {
        public uint ClientUniqueId { get; private set; }
        public uint ActivateUnitId { get; private set; }

        public void Read(GamePacketReader reader)
        {
            ClientUniqueId = reader.ReadUInt();
            ActivateUnitId = reader.ReadUInt();
        }
    }
}
