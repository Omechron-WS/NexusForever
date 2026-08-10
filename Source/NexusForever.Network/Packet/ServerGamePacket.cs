using NexusForever.Network.Message;

namespace NexusForever.Network.Packet
{
    public class ServerGamePacket
    {
        public const ushort HeaderSize = sizeof(uint) + sizeof(ushort);

        /// <summary>
        /// Total size including the header and payload.
        /// </summary>
        public uint Size { get; protected set; }
        public GameMessageOpcode Opcode { get; protected set; }
        public byte[] Data { get; protected set; }

        /// <summary>
        /// Build a bounded server game packet for the supplied opcode and message.
        /// </summary>
        public ServerGamePacket(GameMessageOpcode opcode, IWritable message)
        {
            using (var stream = new MemoryStream())
            using (var writer = new GamePacketWriter(stream))
            {
                message.Write(writer);
                writer.FlushBits();
                Data = stream.ToArray();
            }

            Opcode = opcode;
            uint size = HeaderSize + (uint)Data.Length;
            if (size > ushort.MaxValue)
                throw new InvalidDataException($"Server game packet size {size} exceeds {ushort.MaxValue} bytes.");

            Size = size;
        }

        /// <summary>
        /// Build the complete build-16042 wire frame for this packet.
        /// </summary>
        public byte[] BuildFrame()
        {
            using var stream = new MemoryStream((int)Size);
            using var writer = new GamePacketWriter(stream);
            writer.Write(Size);
            writer.Write(Opcode, 16);
            writer.WriteBytes(Data);
            return stream.ToArray();
        }
    }
}
