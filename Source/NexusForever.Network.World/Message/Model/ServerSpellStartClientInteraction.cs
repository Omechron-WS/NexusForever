using NexusForever.Network.Message;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Message.Model.Shared;

namespace NexusForever.Network.World.Message.Model
{
    /// <summary>
    /// Starts a client-side interaction for a server-selected activation spell.
    /// </summary>
    [Message(GameMessageOpcode.ServerSpellStartClientInteraction)]
    public class ServerSpellStartClientInteraction : IWritable
    {
        public uint ClientUniqueId { get; set; }
        public uint CastingId { get; set; }
        public uint CasterId { get; set; }
        public Position Position { get; set; } = new();
        public uint Yaw { get; set; }

        public List<InitialPosition> InitialPositionData { get; set; } = new();
        public List<TelegraphPosition> TelegraphPositionData { get; set; } = new();

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ClientUniqueId);
            writer.Write(CastingId);
            writer.Write(CasterId);
            Position.Write(writer);
            writer.Write(Yaw);

            writer.Write(InitialPositionData.Count, 8u);
            InitialPositionData.ForEach(position => position.Write(writer));

            writer.Write(TelegraphPositionData.Count, 8u);
            TelegraphPositionData.ForEach(position => position.Write(writer));
        }
    }
}
