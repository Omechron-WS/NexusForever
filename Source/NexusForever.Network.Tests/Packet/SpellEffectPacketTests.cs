using NexusForever.Game.Static.Spell;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;

namespace NexusForever.Network.Tests.Packet
{
    public sealed class SpellEffectPacketTests
    {
        [Fact]
        public void Server07F8_WritesBuild16042PackedActivationLayout()
        {
            var message = new Server07F8
            {
                CastingId      = 0x01020304u,
                Spell4EffectId = 0x54321u,
                TargetId       = 0x11121314u,
                DamageDescriptionData =
                {
                    new TargetInfo.EffectInfo.DamageDescription
                    {
                        KilledTarget = true,
                        CombatResult = CombatResult.OutsideTelegraphInvalid,
                        DamageType   = DamageType.HealShields
                    }
                }
            };

            byte[] payload;
            using (var stream = new MemoryStream())
            {
                using (var writer = new GamePacketWriter(stream))
                {
                    message.Write(writer);
                    writer.FlushBits();
                    payload = stream.ToArray();
                }
            }

            byte[] expected =
            [
                0x04, 0x03, 0x02, 0x01, 0x21, 0x43, 0xA5, 0x98,
                0x90, 0x88, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x58,
                0x05, 0x00
            ];
            Assert.Equal(expected, payload);
        }
    }
}
