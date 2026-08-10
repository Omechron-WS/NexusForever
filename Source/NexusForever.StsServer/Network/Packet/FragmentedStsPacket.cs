using System;
using System.Globalization;
using System.IO;
using NexusForever.Shared;

namespace NexusForever.StsServer.Network.Packet
{
    public class FragmentedStsPacket
    {
        private const int MaximumHeaderLength = 8192;
        private const uint MaximumBodyLength = ushort.MaxValue;

        public bool HasHeader => packet != null;
        public bool HasBody => packet?.Body != null;

        private readonly byte[] headerBuffer = new byte[MaximumHeaderLength];
        private byte[] bodyBuffer;
        private int position;

        private ClientStsPacket packet;
        private uint dataLength;

        /// <summary>
        /// Populate the packet with as much data as is available from the supplied reader.
        /// </summary>
        public void Populate(BinaryReader reader)
        {
            if (!HasHeader)
            {
                while (reader.BaseStream.Remaining() != 0)
                {
                    if (position >= headerBuffer.Length)
                        throw new InvalidDataException($"STS packet header exceeds {MaximumHeaderLength} bytes.");

                    headerBuffer[position++] = reader.ReadByte();
                    if (position < sizeof(uint))
                        continue;

                    // end of header is marked by \r\n\r\n
                    if (BitConverter.ToUInt32(headerBuffer, position - sizeof(uint)) != 0x0A0D0A0Du)
                        continue;

                    packet   = new ClientStsPacket(headerBuffer[..position]);
                    position = 0;

                    if (!uint.TryParse(packet.Headers["l"], NumberStyles.None, CultureInfo.InvariantCulture, out dataLength))
                        throw new InvalidDataException("STS packet contains an invalid length header.");

                    if (dataLength > MaximumBodyLength)
                        throw new InvalidDataException($"STS packet body exceeds {MaximumBodyLength} bytes.");

                    bodyBuffer = new byte[dataLength];
                    if (dataLength == 0)
                        packet.SetBody(bodyBuffer, 0);

                    break;
                }

                if (!HasHeader && position == headerBuffer.Length)
                    throw new InvalidDataException($"STS packet header exceeds {MaximumHeaderLength} bytes.");
            }

            if (!HasHeader || HasBody || reader.BaseStream.Remaining() == 0)
                return;

            uint remainingBody = dataLength - (uint)position;
            int readLength = (int)Math.Min(reader.BaseStream.Remaining(), remainingBody);
            byte[] data = reader.ReadBytes(readLength);
            if (data.Length != readLength)
                throw new EndOfStreamException("Unexpected end of STS packet body.");

            Buffer.BlockCopy(data, 0, bodyBuffer, position, readLength);
            position += readLength;

            if (position == dataLength)
                packet.SetBody(bodyBuffer, dataLength);
        }

        /// <summary>
        /// Return the partially or fully populated client packet.
        /// </summary>
        public ClientStsPacket GetPacket()
        {
            return packet;
        }
    }
}
