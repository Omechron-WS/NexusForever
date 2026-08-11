using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Shared;

namespace NexusForever.Network.World.Message.Model
{
    /// <summary>
    /// Publishes the result of a later build-16042 spell-effect activation.
    /// </summary>
    [Message(GameMessageOpcode.Server07F8)]
    public class Server07F8 : IWritable
    {
        public uint CastingId { get; set; }
        public uint Spell4EffectId { get; set; }
        public uint TargetId { get; set; }
        public List<TargetInfo.EffectInfo.DamageDescription> DamageDescriptionData { get; set; } = [];

        public void Write(GamePacketWriter writer)
        {
            writer.Write(CastingId);
            writer.Write(Spell4EffectId, 19u);
            writer.Write(TargetId);

            writer.Write(DamageDescriptionData.Count, 8u);
            DamageDescriptionData.ForEach(description => description.Write(writer));
        }
    }
}
