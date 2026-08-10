using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using NexusForever.Cryptography;
using NexusForever.Database.Auth.Model;
using NexusForever.Network.Session;
using NexusForever.Network.Sts;
using NexusForever.Network.Sts.Model;
using NexusForever.Shared;
using NexusForever.StsServer.Network.Message;
using NexusForever.StsServer.Network.Packet;

namespace NexusForever.StsServer.Network
{
    public class StsSession : NetworkSession, IStsSession
    {
        public AccountModel Account { get; set; }
        public SessionState State { get; set; }

        public Srp6Provider KeyExchange { get; set; }

        private Arc4Provider clientEncryption;
        private Arc4Provider serverEncryption;
        private Arc4Provider serverNewEncryption;

        private FragmentedStsPacket onDeck;
        private readonly ConcurrentQueue<ClientStsPacket> incomingPackets = new();
        private readonly Queue<ServerStsPacket> outgoingPackets = new();

        private uint sequence;

        #region Dependency Injection

        private readonly IMessageManager messageManager;

        public StsSession(
            IMessageManager messageManager)
        {
            this.messageManager = messageManager;
        }

        #endregion

        public void EnqueueMessageOk(IWritable message)
        {
            EnqueueMessage(200, "OK", message);
        }

        public void EnqueueMessageError(ServerErrorMessage message)
        {
            EnqueueMessage(400, "Bad Request", message);
        }

        public void EnqueueMessage(uint statusCode, string status, IWritable message)
        {
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent             = true,
                IndentChars        = "",
                NewLineChars       = "\n",
                Encoding           = Encoding.UTF8
            };

            using (var stringWriter = new StringWriter())
            using (var writer = XmlWriter.Create(stringWriter, settings))
            {
                writer.WriteStartDocument();
                message.Write(writer);
                writer.WriteEndDocument();
                writer.Flush();

                var packet = new ServerStsPacket(statusCode, status, stringWriter.ToString(), sequence, serverEncryption != null);
                outgoingPackets.Enqueue(packet);
            }
        }

        protected override uint OnData(byte[] data)
        {
            clientEncryption?.Decrypt(data);

            using (var stream = new MemoryStream(data))
            using (var reader = new BinaryReader(stream))
            {
                while (stream.Remaining() != 0)
                {
                    // no packet on deck waiting for additional information, new data will be part of a new packet
                    if (onDeck == null)
                        onDeck = new FragmentedStsPacket();

                    onDeck.Populate(reader);
                    if (onDeck.HasHeader && onDeck.HasBody)
                    {
                        incomingPackets.Enqueue(onDeck.GetPacket());
                        onDeck = null;
                    }
                }
            }

            return 0u;
        }

        public override void Update(double lastTick)
        {
            // process pending packet queue
            while (!IsDisconnecting && incomingPackets.TryDequeue(out ClientStsPacket packet))
            {
                try
                {
                    HandlePacket(packet);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to handle STS packet for session {Id}.");
                    incomingPackets.Clear();
                    outgoingPackets.Clear();
                    ForceDisconnect();
                    break;
                }
            }

            // flush pending packet queue
            while (outgoingPackets.TryDequeue(out ServerStsPacket packet))
                FlushPacket(packet);

            base.Update(lastTick);
        }

        private void HandlePacket(ClientStsPacket packet)
        {
            IReadable message = messageManager.GetMessage(packet.Uri);
            if (message == null)
            {
                log.Info($"Received unknown packet {packet.Uri}");
                return;
            }

            MessageHandlerInfo handlerInfo = messageManager.GetMessageHandler(packet.Uri);
            if (handlerInfo == null)
            {
                log.Info($"Received unhandled packet {packet.Uri}");
                return;
            }

            if (handlerInfo.States.Count != 0 && !handlerInfo.States.Contains(State))
                throw new InvalidDataException($"Received STS packet {packet.Uri} in invalid session state {State}.");

            if (packet.Headers.TryGetValue("s", out string sequenceString))
                uint.TryParse(sequenceString, out sequence);

            log.Trace($"Received packet {packet.Uri}.");

            if (packet.Body != "")
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing            = DtdProcessing.Prohibit,
                    MaxCharactersInDocument = ushort.MaxValue,
                    XmlResolver             = null
                };
                using var stringReader = new StringReader(packet.Body);
                using XmlReader xmlReader = XmlReader.Create(stringReader, settings);
                var doc = new XmlDocument
                {
                    XmlResolver = null
                };
                doc.Load(xmlReader);
                message.Read(doc);
            }

            handlerInfo.Delegate.Invoke(this, message);
        }

        private void FlushPacket(ServerStsPacket packet)
        {
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(packet.Protocol);
                writer.Write(" ");
                writer.Write(packet.StatusCode);
                writer.Write(" ");
                writer.Write(" ");
                writer.Write(packet.Status);
                writer.Write("\r\n");

                foreach ((string name, string value) in packet.Headers)
                {
                    writer.Write($"{name}:{value}");
                    writer.Write("\r\n");
                }

                writer.Write("\r\n");
                writer.Write(packet.Body);
                writer.Flush();

                byte[] buffer = stream.ToArray();
                if (packet.Encrypt)
                    serverEncryption.Encrypt(buffer);

                SendRaw(buffer);
            }

            if (serverNewEncryption != null)
            {
                serverEncryption = serverNewEncryption;
                serverNewEncryption = null;
            }

            log.Trace($"Sent packet response {packet.StatusCode}, {packet.Status}");
        }

        /// <summary>
        /// Initialise client encryption and stage server encryption until the current response is flushed.
        /// </summary>
        public void InitialiseEncryption(byte[] key)
        {
            clientEncryption = new Arc4Provider(key);
            serverNewEncryption = new Arc4Provider(key);
            log.Trace("Initialised RC4 encryption.");
        }

        internal void FailAuthentication(Exception exception)
        {
            log.Error(exception, $"Failed to authenticate STS session {Id}.");
            incomingPackets.Clear();
            outgoingPackets.Clear();
            ForceDisconnect();
        }
    }
}
