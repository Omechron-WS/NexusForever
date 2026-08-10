using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.Extensions.Options;
using NexusForever.Cryptography;
using NexusForever.Database.Auth.Model;
using NexusForever.Network.Configuration.Model;
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

        private FragmentedStsPacket onDeck;
        private readonly ConcurrentQueue<ClientStsPacket> incomingPackets = new();
        private readonly object outgoingGate = new();

        private uint sequence;

        #region Dependency Injection

        private readonly IMessageManager messageManager;

        public StsSession(
            IMessageManager messageManager,
            IOptions<NetworkConfig> networkOptions)
            : base(networkOptions)
        {
            this.messageManager = messageManager;
        }

        internal StsSession(
            IMessageManager messageManager,
            IOptions<NetworkConfig> networkOptions,
            ISocketSendAdapter socketSendAdapter)
            : base(networkOptions, socketSendAdapter)
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
            if (IsDisconnecting)
                return;

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

                lock (outgoingGate)
                {
                    if (IsDisconnecting)
                        return;

                    var packet = new ServerStsPacket(
                        statusCode,
                        status,
                        stringWriter.ToString(),
                        sequence,
                        serverEncryption != null);
                    byte[] buffer = packet.BuildFrame();
                    if (packet.Encrypt)
                        serverEncryption.Encrypt(buffer);

                    if (!TryQueueRaw(buffer))
                    {
                        ForceDisconnect();
                        return;
                    }

                    log.Trace($"Sent packet response {packet.StatusCode}, {packet.Status}");
                }
            }
        }

        protected override uint OnData(byte[] data)
        {
            if (IsDisconnecting)
                return 0u;

            Volatile.Read(ref clientEncryption)?.Decrypt(data);

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
                    ForceDisconnect();
                    break;
                }
            }

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

        /// <summary>
        /// Initialise client and server encryption after the current response has been staged.
        /// </summary>
        public void InitialiseEncryption(byte[] key)
        {
            lock (outgoingGate)
            {
                if (IsDisconnecting)
                    return;

                Volatile.Write(ref clientEncryption, new Arc4Provider(key));
                serverEncryption = new Arc4Provider(key);
                log.Trace("Initialised RC4 encryption.");
            }
        }

        internal void DisconnectAfterPendingSends()
        {
            lock (outgoingGate)
            {
                incomingPackets.Clear();
                RequestDisconnectAfterPendingSends();
            }
        }

        internal void FailAuthentication(Exception exception)
        {
            log.Error(exception, $"Failed to authenticate STS session {Id}.");
            incomingPackets.Clear();
            ForceDisconnect();
        }
    }
}
