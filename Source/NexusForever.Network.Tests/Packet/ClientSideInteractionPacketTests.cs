using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Game.Static.CSI;

namespace NexusForever.Network.Tests.Packet
{
    public class ClientSideInteractionPacketTests
    {
        [Fact]
        public void Build16042Opcodes_AreStable()
        {
            Assert.Equal(0x0098, (ushort)GameMessageOpcode.ClientActivateUnitInteraction);
            Assert.Equal(0x07FD, (ushort)GameMessageOpcode.ServerSpellStartClientInteraction);
            Assert.Equal(0x0805, (ushort)GameMessageOpcode.ClientInteractionResult);
        }

        [Fact]
        public void ClientActivateUnitInteraction_ReadsTwoUInts()
        {
            byte[] payload =
            [
                0x04, 0x03, 0x02, 0x01,
                0x14, 0x13, 0x12, 0x11
            ];
            var message = new ClientActivateUnitInteraction();

            using var reader = new GamePacketReader(new MemoryStream(payload));
            message.Read(reader);

            Assert.Equal(0x01020304u, message.ClientUniqueId);
            Assert.Equal(0x11121314u, message.ActivateUnitId);
            Assert.Equal(0u, reader.BytesRemaining);
        }

        [Fact]
        public void ClientSpellInteractionResult_ReadsPackedThreeBitResult()
        {
            byte[] payload =
            [
                0x12, 0x34, 0x56, 0x78,
                0xA5, 0x1E, 0x96, 0x0D, 0x05
            ];
            var message = new ClientSpellInteractionResult();

            using var reader = new GamePacketReader(new MemoryStream(payload));
            message.Read(reader);

            Assert.Equal(0x78563412u, message.CastingId);
            Assert.Equal((ClientSideInteractionResult)5, message.Result);
            Assert.Equal(0xA1B2C3D4u, message.Validation);
        }

        [Fact]
        public void ServerSpellStartClientInteraction_WritesBuild16042Layout()
        {
            var message = new ServerSpellStartClientInteraction
            {
                ClientUniqueId = 0x01020304u,
                CastingId = 0x11121314u,
                CasterId = 0x21222324u,
                Yaw = 0x31323334u
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
                0x04, 0x03, 0x02, 0x01,
                0x14, 0x13, 0x12, 0x11,
                0x24, 0x23, 0x22, 0x21,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x34, 0x33, 0x32, 0x31,
                0x00,
                0x00
            ];
            Assert.Equal(expected, payload);
        }
    }
}
