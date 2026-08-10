using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NexusForever.Network.Configuration.Model;
using NexusForever.Network.Session.Static;
using NexusForever.Shared.Game.Events;
using NLog;

namespace NexusForever.Network.Session
{
    public abstract class NetworkSession : INetworkSession
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Unique id for <see cref="NetworkSession"/>.
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// <see cref="IEvent"/> queue that will be processed during <see cref="NetworkSession"/> update.
        /// </summary>
        public EventQueue Events { get; } = new();

        /// <summary>
        /// Heartbeat to check if <see cref="NetworkSession"/> is still alive.
        /// </summary>
        /// <remarks>
        /// If <see cref="SocketHeartbeat"/> flatlines the <see cref="NetworkSession"/> will be disconnected.
        /// </remarks>
        public SocketHeartbeat Heartbeat { get; } = new();

        private readonly NetworkConfig networkConfig;
        private readonly ISocketSendAdapter socketSendAdapter;

        private Socket socket;
        private BoundedSocketWriter socketWriter;

        private readonly byte[] buffer = new byte[4096];
        private int bufferOffset;

        private int disconnectState = (int)DisconnectState.Connected;
        private double pendingSendDrainElapsed;
        private int sendFailureObserved;

        protected NetworkSession(IOptions<NetworkConfig> networkOptions)
            : this(networkOptions, null)
        {
        }

        internal NetworkSession(
            IOptions<NetworkConfig> networkOptions,
            ISocketSendAdapter socketSendAdapter)
        {
            ArgumentNullException.ThrowIfNull(networkOptions);

            networkConfig          = networkOptions.Value;
            this.socketSendAdapter = socketSendAdapter;
        }

        /// <summary>
        /// Returns whether disconnection has been requested or is in progress.
        /// </summary>
        protected bool IsDisconnecting => ConnectionState != DisconnectState.Connected;

        internal DisconnectState ConnectionState => (DisconnectState)Volatile.Read(ref disconnectState);
        internal Task OutboundSendCompletion => socketWriter?.Completion ?? Task.CompletedTask;
        internal long PendingSendBytes => socketWriter?.PendingBytes ?? 0L;
        internal int PendingSendFrames => socketWriter?.PendingFrames ?? 0;

        /// <summary>
        /// Initialise <see cref="NetworkSession"/> with new <see cref="Socket"/> and begin listening for data.
        /// </summary>
        public virtual void OnAccept(Socket newSocket)
        {
            if (socket != null)
                throw new InvalidOperationException();

            Id = Guid.NewGuid().ToString();

            socket = newSocket;
            socketWriter = new BoundedSocketWriter(
                socketSendAdapter ?? new SocketSendAdapter(newSocket),
                networkConfig.MaximumPendingSendBytes,
                networkConfig.MaximumPendingSendFrames);
            socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveDataCallback, null);

            log.Trace($"New client {Id} connected from {newSocket.RemoteEndPoint}.");
        }

        /// <summary>
        /// Update <see cref="NetworkSession"/> existing id with a new supplied id.
        /// </summary>
        /// <remarks>
        /// This should be used when the default session id can be replaced with a known unique id.
        /// </remarks>
        public void UpdateId(string id)
        {
            log.Trace($"Client {Id} updated id to {id}.");
            Id = id;
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public virtual void Update(double lastTick)
        {
            Events.Update(lastTick);
            ObserveSocketWriter(lastTick);

            if (ConnectionState == DisconnectState.Connected)
                Heartbeat.Update(lastTick);

            DisconnectState state = ConnectionState;
            if (state is DisconnectState.Complete or DisconnectState.Processing or DisconnectState.Draining)
                return;

            if (Heartbeat.Flatline && state == DisconnectState.Connected)
            {
                log.Trace($"Client {Id} has flatlined.");
                ForceDisconnect();
                state = ConnectionState;
            }

            if (state == DisconnectState.Pending
                && Interlocked.CompareExchange(
                    ref disconnectState,
                    (int)DisconnectState.Processing,
                    (int)DisconnectState.Pending) == (int)DisconnectState.Pending)
                OnDisconnect();
        }

        protected virtual void OnDisconnect()
        {
            socketWriter?.Abort();

            try
            {
                EndPoint remoteEndPoint = socket.RemoteEndPoint;
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();

                log.Trace($"Client {Id} disconnected. {remoteEndPoint}");
            }
            catch (Exception e)
            {
                log.Error(e, $"An exception occured for client {Id} during socket close!");
            }

            Volatile.Write(ref disconnectState, (int)DisconnectState.Complete);
        }

        /// <summary>
        /// Returns if <see cref="NetworkSession"/> can be disposed.
        /// </summary>
        public virtual bool CanDispose()
        {
            return ConnectionState == DisconnectState.Complete
                && !Events.PendingEvents
                && (socketWriter?.Completion.IsCompleted ?? true);
        }

        /// <summary>
        /// Invoked with <see cref="IAsyncResult"/> when data from the <see cref="Socket"/> is received.
        /// </summary>
        private void ReceiveDataCallback(IAsyncResult ar)
        {
            try
            {
                int length = socket.EndReceive(ar);
                if (length == 0)
                {
                    ForceDisconnect();
                    return;
                }

                if (IsDisconnecting)
                    return;

                byte[] data = new byte[length + bufferOffset];
                Buffer.BlockCopy(buffer, 0, data, 0, data.Length);
                bufferOffset = (int)OnData(data);

                // if we have data that wasn't processed move it to the start of the buffer
                // any new data will be amended to it
                if (bufferOffset != 0)
                    Buffer.BlockCopy(buffer, data.Length - bufferOffset, buffer, 0, bufferOffset);

                if (!IsDisconnecting)
                    socket.BeginReceive(buffer, bufferOffset, buffer.Length - bufferOffset, SocketFlags.None, ReceiveDataCallback, null);
            }
            catch (Exception e)
            {
                log.Error(e, $"An exception occured for client {Id} during socket read!");
                ForceDisconnect();
            }
        }

        protected abstract uint OnData(byte[] data);

        /// <summary>
        /// Queue supplied data to be sent to the remote client without blocking the caller.
        /// </summary>
        protected bool TryQueueRaw(byte[] data)
        {
            if (IsDisconnecting || socketWriter == null)
                return false;

            return socketWriter.TryEnqueue(data);
        }

        /// <summary>
        /// Stop accepting outbound frames and disconnect after all admitted frames have been sent.
        /// </summary>
        protected void RequestDisconnectAfterPendingSends()
        {
            if (Interlocked.CompareExchange(
                ref disconnectState,
                (int)DisconnectState.Draining,
                (int)DisconnectState.Connected) != (int)DisconnectState.Connected)
                return;

            pendingSendDrainElapsed = 0d;
            socketWriter?.Complete();
        }

        /// <summary>
        /// Force disconnect of <see cref="NetworkSession"/>.
        /// </summary>
        public void ForceDisconnect()
        {
            while (true)
            {
                DisconnectState state = ConnectionState;
                if (state is DisconnectState.Pending or DisconnectState.Processing or DisconnectState.Complete)
                    return;

                if (Interlocked.CompareExchange(
                    ref disconnectState,
                    (int)DisconnectState.Pending,
                    (int)state) == (int)state)
                    return;
            }
        }

        private void ObserveSocketWriter(double lastTick)
        {
            if (socketWriter == null)
                return;

            Task completion = socketWriter.Completion;
            if (completion.IsFaulted && Interlocked.Exchange(ref sendFailureObserved, 1) == 0)
            {
                log.Error(completion.Exception?.GetBaseException(), $"An exception occured for client {Id} during socket send!");
                ForceDisconnect();
            }
            else if (completion.IsCanceled && ConnectionState == DisconnectState.Connected)
                ForceDisconnect();

            if (ConnectionState != DisconnectState.Draining)
                return;

            if (completion.IsCompletedSuccessfully)
            {
                Interlocked.CompareExchange(
                    ref disconnectState,
                    (int)DisconnectState.Pending,
                    (int)DisconnectState.Draining);
                return;
            }

            pendingSendDrainElapsed += lastTick;
            if (pendingSendDrainElapsed >= networkConfig.PendingSendDrainTimeoutSeconds)
                ForceDisconnect();
        }
    }
}
