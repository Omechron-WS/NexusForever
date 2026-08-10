using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexusForever.Cryptography;
using NexusForever.Network.Configuration.Model;
using NexusForever.Network.Message;
using NexusForever.Network.Packet;
using NexusForever.Shared;

namespace NexusForever.Network.Session
{
    public abstract class GameSession : NetworkSession, IGameSession
    {
        private const uint MinimumPacketSize = sizeof(uint) + sizeof(ushort);
        private const uint MaximumPacketSize = ushort.MaxValue;
        private const int MaximumPacketNestingDepth = 4;

        /// <summary>
        /// Determines if queued incoming packets can be processed during a world update.
        /// </summary>
        public bool CanProcessIncomingPackets { get; set; } = true;

        /// <summary>
        /// Determines if queued outgoing packets can be processed during a world update.
        /// </summary>
        public bool CanProcessOutgoingPackets { get; set; } = true;

        protected PacketCrypt encryption;

        private FragmentedBuffer onDeck;
        private int packetHandlingDepth;
        private readonly ConcurrentQueue<ClientGamePacket> incomingPackets = new();
        private readonly object outgoingGate = new();

        #region Dependency Injection

        private readonly IMessageManager messageManager;

        public GameSession(
            IMessageManager messageManager,
            IOptions<NetworkConfig> networkOptions)
            : base(networkOptions)
        {
            this.messageManager = messageManager;
        }

        internal GameSession(
            IMessageManager messageManager,
            IOptions<NetworkConfig> networkOptions,
            ISocketSendAdapter socketSendAdapter)
            : base(networkOptions, socketSendAdapter)
        {
            this.messageManager = messageManager;
        }

        #endregion

        /// <summary>
        /// Enqueue <see cref="IWritable"/> to be sent to the client.
        /// </summary>
        public void EnqueueMessage(IWritable message)
        {
            if (TryEnqueueMessage(message, out GameMessageOpcode? opcode)
                && opcode != GameMessageOpcode.ServerAuthEncrypted
                && opcode != GameMessageOpcode.ServerRealmEncrypted)
                log.Trace($"Sent packet {opcode}(0x{opcode:X}).");
        }

        /// <summary>
        /// Enqueue <see cref="IWritable"/> to be sent encrypted to the client.
        /// </summary>
        public void EnqueueMessageEncrypted(IWritable message)
        {
            if (!CanProcessOutgoingPackets || IsDisconnecting)
                return;

            GameMessageOpcode? opcode = messageManager.GetOpcode(message);
            if (opcode == null)
            {
                log.Warn("Failed to send message with no attribute!");
                return;
            }

            byte[] data;
            using (var stream = new MemoryStream())
            using (var writer = new GamePacketWriter(stream))
            {
                writer.Write(opcode.Value, 16);
                message.Write(writer);
                writer.FlushBits();
                data = stream.ToArray();
            }

            bool queued;
            lock (outgoingGate)
            {
                if (!CanProcessOutgoingPackets || IsDisconnecting)
                    return;

                byte[] encrypted = encryption.Encrypt(data, data.Length);
                queued = TryEnqueueMessage(BuildEncryptedMessage(encrypted), out _);
            }

            if (queued)
                log.Trace($"Sent packet {opcode}(0x{opcode:X}).");
        }

        public void EnqueueMessageEncrypted(uint opcode, string hex)
        {
            if (!CanProcessOutgoingPackets || IsDisconnecting)
                return;

            byte[] data;
            using (var stream = new MemoryStream())
            using (var writer = new GamePacketWriter(stream))
            {
                writer.Write(opcode, 16);

                byte[] body = Enumerable.Range(0, hex.Length)
                    .Where(x => x % 2 == 0)
                    .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                    .ToArray();
                writer.WriteBytes(body);

                writer.FlushBits();
                data = stream.ToArray();
            }

            lock (outgoingGate)
            {
                if (!CanProcessOutgoingPackets || IsDisconnecting)
                    return;

                byte[] encrypted = encryption.Encrypt(data, data.Length);
                TryEnqueueMessage(BuildEncryptedMessage(encrypted), out _);
            }
        }

        protected abstract IWritable BuildEncryptedMessage(byte[] data);

        public override void OnAccept(Socket newSocket)
        {
            base.OnAccept(newSocket);

            ulong key = PacketCrypt.GetKeyFromAuthBuildAndMessage();
            SetEncryption(new PacketCrypt(key));
        }

        protected override uint OnData(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            using (var reader = new GamePacketReader(stream))
            {
                while (stream.Remaining() != 0)
                {
                    // no packet on deck waiting for additional information, new data will be part of a new packet
                    if (onDeck == null)
                    {
                        if (stream.Remaining() < sizeof(uint))
                        {
                            // we don't have enough data to know the length of the next packet
                            // return the remaining buffer so new data can be appended
                            return stream.Remaining();
                        }

                        uint size = reader.ReadUInt();
                        if (size < MinimumPacketSize || size > MaximumPacketSize)
                            throw new InvalidDataException($"Invalid game packet size {size}.");

                        onDeck = new FragmentedBuffer(size - sizeof(uint));
                    }

                    onDeck.Populate(reader);
                    if (onDeck.IsComplete)
                    {
                        incomingPackets.Enqueue(new ClientGamePacket
                        {
                            Data = onDeck.Data,
                            IsEncrypted = false
                        });
                        onDeck = null;
                    }
                }
            }

            return 0u;
        }

        protected override void OnDisconnect()
        {
            // clear any pending packets and prevent any new packets from being processed
            CanProcessIncomingPackets = false;
            CanProcessOutgoingPackets = false;
            incomingPackets.Clear();

            base.OnDisconnect();
        }

        public override void Update(double lastTick)
        {
            base.Update(lastTick);

            // process pending packet queue
            while (!IsDisconnecting && CanProcessIncomingPackets && incomingPackets.TryDequeue(out ClientGamePacket packet))
                HandlePacket(packet);
        }

        /// <summary>
        /// Parse and dispatch a client packet while enforcing the nested-packet limit.
        /// </summary>
        public void HandlePacket(ClientGamePacket packet)
        {
            if (IsDisconnecting || !CanProcessIncomingPackets)
                return;

            if (packetHandlingDepth >= MaximumPacketNestingDepth)
            {
                FailPacket(new InvalidPacketValueException($"Client packet nesting exceeds {MaximumPacketNestingDepth} levels."));
                return;
            }

            packetHandlingDepth++;
            try
            {
                //using IServiceScope serviceScope = CreateHandlePacketScope();
                var serviceProvider = LegacyServiceProvider.Provider;

                using var reader = new ClientGamePacketReader();
                reader.Initialise(packet, encryption);
                GameMessageOpcode opcode = reader.ReadHeader();

                //IReadable message = serviceScope.ServiceProvider.GetKeyedService<IReadable>(opcode);
                IReadable message = serviceProvider.GetKeyedService<IReadable>(opcode);
                if (message == null)
                {
                    log.Warn($"Received unknown packet {opcode}(0x{opcode:X}.");
                    return;
                }

                Type handlerType = messageManager.GetMessageHandlerType(opcode);
                if (handlerType == null)
                {
                    log.Warn($"Received unhandled packet {opcode}(0x{opcode:X}).");
                    return;
                }

                //object handler = serviceScope.ServiceProvider.GetService(handlerType);
                object handler = serviceProvider.GetService(handlerType);
                if (handler == null)
                {
                    log.Warn($"Received unhandled packet {opcode}(0x{opcode:X}).");
                    return;
                }

                MessageHandlerDelegate handlerDelegate = messageManager.GetMessageHandlerDelegate(opcode);
                if (handlerDelegate == null)
                {
                    log.Warn($"Received unhandled packet {opcode}(0x{opcode:X}).");
                    return;
                }

                if (opcode != GameMessageOpcode.ClientEncrypted
                    && opcode != GameMessageOpcode.ClientPacked
                    && opcode != GameMessageOpcode.ClientPackedWorld
                    && opcode != GameMessageOpcode.ClientEntityCommand)
                    log.Trace($"Received packet {opcode}(0x{opcode:X}).");

                // FIXME workaround for now. possible performance impact. 
                // ClientPing does not currently work and the session times out after 300s -> this keeps the session alive if -any- client packet is received
                Heartbeat.OnHeartbeat();

                uint remaining = reader.ReadBody(message);
                if (remaining > 0)
                    log.Warn($"Failed to read entire contents of packet {opcode}");

                handlerDelegate.Invoke(handler, this, message);
            }
            catch (InvalidPacketValueException exception)
            {
                FailPacket(exception);
            }
            catch (Exception exception)
            {
                FailPacket(exception);
            }
            finally
            {
                packetHandlingDepth--;
            }
        }

        private void FailPacket(Exception exception)
        {
            log.Error(exception);
            CanProcessIncomingPackets = false;
            CanProcessOutgoingPackets = false;
            incomingPackets.Clear();
            ForceDisconnect();
        }

        protected virtual IServiceScope CreateHandlePacketScope()
        {
            return LegacyServiceProvider.Provider.CreateScope();
        }

        protected void SetEncryption(PacketCrypt packetCrypt)
        {
            ArgumentNullException.ThrowIfNull(packetCrypt);

            lock (outgoingGate)
                encryption = packetCrypt;
        }

        private bool TryEnqueueMessage(IWritable message, out GameMessageOpcode? opcode)
        {
            if (!CanProcessOutgoingPackets || IsDisconnecting)
            {
                opcode = null;
                return false;
            }

            opcode = messageManager.GetOpcode(message);
            if (opcode == null)
            {
                log.Warn("Failed to send message with no attribute!");
                return false;
            }

            var packet = new ServerGamePacket(opcode.Value, message);
            lock (outgoingGate)
            {
                if (!CanProcessOutgoingPackets || IsDisconnecting)
                    return false;

                if (TryQueueRaw(packet.BuildFrame()))
                    return true;

                CanProcessOutgoingPackets = false;
                ForceDisconnect();
                return false;
            }
        }
    }
}
