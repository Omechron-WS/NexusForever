using System.Numerics;
using NexusForever.Game.Static.Entity.Movement;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Network.Message;
using NexusForever.Network.World.Entity.Command;
using NexusForever.Network.World.Message.Model.Entity;

namespace NexusForever.Network.Tests.Packet
{
    public class MovementPacketTests
    {
        [Fact]
        public void Build16042DashOpcode_IsStable()
        {
            Assert.Equal(0x00DE, (ushort)GameMessageOpcode.ClientDash);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public void ClientDash_ReadsExactlyThreeBitDirection(int encodedDirection)
        {
            byte[] payload = [(byte)(0b1011_1000 | encodedDirection)];
            var message = new ClientDash();

            using var reader = new GamePacketReader(new MemoryStream(payload));
            message.Read(reader);

            Assert.Equal((DashDirection)encodedDirection, message.Direction);
            Assert.Equal((byte)0b10111, reader.ReadByte(5u));
            Assert.Equal(0u, reader.BytesRemaining);
        }

        [Fact]
        public void ClientDash_EmptyPayloadIsRejected()
        {
            var message = new ClientDash();
            using var reader = new GamePacketReader(new MemoryStream([]));

            Assert.Throws<InvalidPacketValueException>(() => message.Read(reader));
        }

        [Fact]
        public void PositionPath_ExactEncodedLimitRoundTrips()
        {
            List<Vector3> sourceNodes = Enumerable.Range(0, SplinePathLimits.MaximumLinearSourceNodeCount)
                .Select(index => new Vector3(index, 0f, 0f))
                .ToList();
            var message = new SetPositionPathCommand
            {
                Positions = [sourceNodes[0], .. sourceNodes, sourceNodes[^1]],
                Speed = 3f,
                Type = SplineType.Linear,
                Mode = SplineMode.OneShot
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

            var decoded = new SetPositionPathCommand();
            using var reader = new GamePacketReader(new MemoryStream(payload));
            decoded.Read(reader);

            Assert.Equal(SplinePathLimits.EncodedPositionCount, decoded.Positions.Count);
            Assert.Equal(message.Positions, decoded.Positions);
            Assert.Equal(message.Type, decoded.Type);
            Assert.Equal(message.Mode, decoded.Mode);
            Assert.Equal(message.Speed, decoded.Speed);
            Assert.Equal(0u, reader.BytesRemaining);
        }

        [Fact]
        public void PositionPath_PackedFloatSpeedBoundariesRoundTrip()
        {
            foreach ((float speed, float expected) in new[]
            {
                (SplinePathLimits.MinimumPackedFloatSpeed, SplinePathLimits.MinimumPackedFloatSpeed),
                (SplinePathLimits.MaximumPackedFloatSpeed, BitConverter.Int32BitsToSingle(0x47FFE000))
            })
            {
                var message = new SetPositionPathCommand
                {
                    Positions = [Vector3.Zero, Vector3.Zero, Vector3.One, Vector3.One],
                    Speed = speed,
                    Type = SplineType.Linear,
                    Mode = SplineMode.OneShot
                };
                using var stream = new MemoryStream();
                using (var writer = new GamePacketWriter(stream))
                {
                    message.Write(writer);
                    writer.FlushBits();
                }

                var decoded = new SetPositionPathCommand();
                using var reader = new GamePacketReader(new MemoryStream(stream.ToArray()));
                decoded.Read(reader);

                Assert.Equal(expected, decoded.Speed);
            }
        }

        [Fact]
        public void PositionPath_OutsidePackedFloatSpeedRangeFailsBeforeWriting()
        {
            foreach (float speed in new[]
            {
                MathF.BitDecrement(SplinePathLimits.MinimumPackedFloatSpeed),
                MathF.BitIncrement(SplinePathLimits.MaximumPackedFloatSpeed)
            })
            {
                var message = new SetPositionPathCommand
                {
                    Positions = [Vector3.Zero, Vector3.Zero, Vector3.One, Vector3.One],
                    Speed = speed,
                    Type = SplineType.Linear,
                    Mode = SplineMode.OneShot
                };
                using var stream = new MemoryStream();
                using var writer = new GamePacketWriter(stream);

                Assert.Throws<ArgumentOutOfRangeException>(() => message.Write(writer));
                Assert.Equal(0, stream.Length);
            }
        }

        [Fact]
        public void PositionPath_AboveEncodedLimitFailsBeforeWriting()
        {
            var message = new SetPositionPathCommand
            {
                Positions = Enumerable.Range(0, SplinePathLimits.EncodedPositionCount + 1)
                    .Select(index => new Vector3(index, 0f, 0f))
                    .ToList(),
                Speed = 3f,
                Type = SplineType.CatmullRom,
                Mode = SplineMode.OneShot
            };
            using var stream = new MemoryStream();
            using var writer = new GamePacketWriter(stream);

            Assert.Throws<ArgumentOutOfRangeException>(() => message.Write(writer));
            Assert.Equal(0, stream.Length);
        }

        [Theory]
        [InlineData(0, 0, 0f)]
        [InlineData(2, 0, 3f)]
        [InlineData(0, 6, 3f)]
        public void PositionPath_InvalidModelFailsBeforeWriting(int type, int mode, float speed)
        {
            var message = new SetPositionPathCommand
            {
                Positions = [Vector3.Zero, Vector3.Zero, Vector3.One, Vector3.One],
                Speed = speed,
                Type = (SplineType)type,
                Mode = (SplineMode)mode
            };
            using var stream = new MemoryStream();
            using var writer = new GamePacketWriter(stream);

            Assert.ThrowsAny<ArgumentException>(() => message.Write(writer));
            Assert.Equal(0, stream.Length);
        }

        [Fact]
        public void PositionPath_NonFinitePositionOrSpeedFailsBeforeWriting()
        {
            foreach (SetPositionPathCommand message in new[]
            {
                new SetPositionPathCommand
                {
                    Positions = [Vector3.Zero, Vector3.Zero, new Vector3(float.NaN), new Vector3(float.NaN)],
                    Speed = 3f,
                    Type = SplineType.Linear,
                    Mode = SplineMode.OneShot
                },
                new SetPositionPathCommand
                {
                    Positions = [Vector3.Zero, Vector3.Zero, Vector3.One, Vector3.One],
                    Speed = float.PositiveInfinity,
                    Type = SplineType.Linear,
                    Mode = SplineMode.OneShot
                }
            })
            {
                using var stream = new MemoryStream();
                using var writer = new GamePacketWriter(stream);
                Assert.ThrowsAny<ArgumentException>(() => message.Write(writer));
                Assert.Equal(0, stream.Length);
            }
        }

        [Fact]
        public void PositionPath_MalformedLinearPaddingFailsBeforeWriting()
        {
            var message = new SetPositionPathCommand
            {
                Positions = [new Vector3(-1f), Vector3.Zero, Vector3.One, new Vector3(2f)],
                Speed = 3f,
                Type = SplineType.Linear,
                Mode = SplineMode.OneShot
            };
            using var stream = new MemoryStream();
            using var writer = new GamePacketWriter(stream);

            Assert.Throws<ArgumentOutOfRangeException>(() => message.Write(writer));
            Assert.Equal(0, stream.Length);
        }
    }
}
