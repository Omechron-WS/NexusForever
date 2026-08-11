using NexusForever.Game.Static.Housing;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Housing;

namespace NexusForever.Network.Tests.Packet
{
    public sealed class HousingPacketTests
    {
        [Fact]
        public void Build16042HousingOpcodes_AreStable()
        {
            Assert.Equal(0x00C9, (ushort)GameMessageOpcode.ClientHousingResidencePrivacyLevel);
            Assert.Equal(0x010E, (ushort)GameMessageOpcode.ServerHousingBasics);
            Assert.Equal(0x0531, (ushort)GameMessageOpcode.ClientHousingVisit);
        }

        [Fact]
        public void ClientHousingResidencePrivacyLevel_ReadsExactUInt32Layout()
        {
            byte[] payload =
            [
                0x03, 0x00, 0x00, 0x00
            ];
            var message = new ClientHousingSetPrivacyLevel();

            using var reader = new GamePacketReader(new MemoryStream(payload));
            message.Read(reader);

            Assert.Equal(ResidencePrivacyLevel.Private, message.PrivacyLevel);
            Assert.Equal(0u, reader.BytesRemaining);
        }

        [Fact]
        public void ClientHousingVisit_ReadsExactBuild16042RequestLayout()
        {
            byte[] payload =
            [
                0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
                0x23, 0x01, 0xC6, 0x85, 0x45, 0x05, 0xC5, 0x84,
                0x44, 0x84, 0x40, 0x10, 0x00, 0x8D, 0x80, 0x72,
                0x62, 0x52, 0x42, 0x32, 0x22, 0x12, 0x22, 0x20,
                0x04, 0x00
            ];
            var message = new ClientHousingVisit();

            using var reader = new GamePacketReader(new MemoryStream(payload));
            message.Read(reader);

            Assert.Equal(0x0102030405060708ul, message.Unused);
            Assert.Equal((ushort)0x0123, message.IdentityToVisit.RealmId);
            Assert.Equal(0x1112131415161718ul, message.IdentityToVisit.Id);
            Assert.Equal("A", message.PlayerToVisitName);
            Assert.Equal((ushort)0x0234, message.CommunityToVisitIdentity.RealmId);
            Assert.Equal(0x2122232425262728ul, message.CommunityToVisitIdentity.Id);
            Assert.Equal("B", message.CommunityToVisitName);
            Assert.Equal(0u, reader.BytesRemaining);
        }

        [Fact]
        public void ServerHousingBasics_WritesResidenceNeighbourhoodThenPrivacy()
        {
            var message = new ServerHousingBasics
            {
                ResidenceId     = 0x0102030405060708ul,
                NeighbourhoodId = 0x1112131415161718ul,
                PrivacyLevel    = ServerHousingBasics.ResidencePrivacyLevelFlags.RoommatesOnly
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
                0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
                0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11,
                0x04, 0x00, 0x00, 0x00
            ];
            Assert.Equal(expected, payload);
        }
    }
}
