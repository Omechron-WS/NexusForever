using NexusForever.Network.Message;

namespace NexusForever.Network.World.Message.Model
{
    [Message(GameMessageOpcode.ClientLootVacuum)]
    public class ClientLootVacuum : IReadable
    {
        public void Read(GamePacketReader reader)
        {
        }
    }
}
