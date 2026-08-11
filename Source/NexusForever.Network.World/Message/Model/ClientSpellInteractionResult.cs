using NexusForever.Game.Static.CSI;
using NexusForever.Network.Message;

namespace NexusForever.Network.World.Message.Model
{
    /// <summary>
    /// Reports the result of a client-side interaction identified by its server casting ID.
    /// </summary>
    [Message(GameMessageOpcode.ClientInteractionResult)]
    public class ClientSpellInteractionResult : IReadable
    {
        public uint CastingId { get; private set; }
        public ClientSideInteractionResult Result { get; private set; }
        public uint Validation { get; private set; }

        public void Read(GamePacketReader reader)
        {
            CastingId  = reader.ReadUInt();
            Result     = reader.ReadEnum<ClientSideInteractionResult>(3u);
            Validation = reader.ReadUInt();
        }
    }
}
