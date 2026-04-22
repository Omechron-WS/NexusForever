using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Shared;

namespace NexusForever.Network.World.Message.Model
{
    [Message(GameMessageOpcode.ServerLootGrant)]
    public class ServerLootGrant : IWritable
    {
        public uint UnitId { get; set; }
        public uint LooterId { get; set; }
        public NetworkLootItem LootItem { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(UnitId);
            writer.Write(LooterId);
            LootItem.Write(writer);
        }
    }
}
