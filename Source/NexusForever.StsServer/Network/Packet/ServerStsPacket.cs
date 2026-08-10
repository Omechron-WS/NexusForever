using System.IO;

namespace NexusForever.StsServer.Network.Packet
{
    public class ServerStsPacket : StsPacket
    {
        public uint StatusCode { get; }
        public string Status { get; }
        public bool Encrypt { get; }

        public ServerStsPacket(uint statusCode, string status, string body, uint sequence, bool encrypt)
        {
            StatusCode = statusCode;
            Status     = status;
            Body       = $"{body}\n";
            Encrypt    = encrypt;
            Protocol   = "STS/1.0";

            Headers.Add("l", Body.Length.ToString());
            Headers.Add("s", $"{sequence}R");
        }

        /// <summary>
        /// Build the complete STS wire frame for this packet.
        /// </summary>
        public byte[] BuildFrame()
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            writer.Write(Protocol);
            writer.Write(" ");
            writer.Write(StatusCode);
            writer.Write(" ");
            writer.Write(" ");
            writer.Write(Status);
            writer.Write("\r\n");

            foreach ((string name, string value) in Headers)
            {
                writer.Write($"{name}:{value}");
                writer.Write("\r\n");
            }

            writer.Write("\r\n");
            writer.Write(Body);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
