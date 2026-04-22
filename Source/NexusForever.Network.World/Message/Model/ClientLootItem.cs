using NexusForever.Network.Message;

namespace NexusForever.Network.World.Message.Model
{
    [Message(GameMessageOpcode.ClientLootItem)]
    public class ClientLootItem : IReadable
    {
        public uint OwnerId { get; private set; }
        public uint LootId { get; private set; }

        public void Read(GamePacketReader reader)
        {
            OwnerId = reader.ReadUInt();
            LootId  = reader.ReadUInt();
        }
    }
}
