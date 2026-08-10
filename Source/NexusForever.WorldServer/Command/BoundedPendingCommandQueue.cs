using System;
using System.Threading;
using System.Threading.Channels;

namespace NexusForever.WorldServer.Command
{
    /// <summary>
    /// Provides non-blocking bounded staging for commands that must run on the world thread.
    /// </summary>
    internal sealed class BoundedPendingCommandQueue
    {
        private readonly Channel<PendingCommand> commands;
        private readonly int maximumPendingCommands;

        private int count;

        /// <summary>
        /// Number of commands currently pending.
        /// </summary>
        public int Count => Volatile.Read(ref count);

        /// <summary>
        /// Create a command queue with the supplied maximum capacity.
        /// </summary>
        public BoundedPendingCommandQueue(int maximumPendingCommands)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPendingCommands);
            this.maximumPendingCommands = maximumPendingCommands;

            commands = Channel.CreateBounded<PendingCommand>(new BoundedChannelOptions(maximumPendingCommands)
            {
                AllowSynchronousContinuations = false,
                FullMode                      = BoundedChannelFullMode.Wait,
                SingleReader                  = false,
                SingleWriter                  = false
            });
        }

        /// <summary>
        /// Attempt to enqueue a delayed command.
        /// </summary>
        /// <returns><see langword="true"/> when the command was admitted; otherwise, <see langword="false"/>.</returns>
        public bool TryEnqueue(PendingCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (!TryReserve())
                return false;

            if (commands.Writer.TryWrite(command))
                return true;

            Interlocked.Decrement(ref count);
            return false;
        }

        /// <summary>
        /// Attempt to remove the next delayed command.
        /// </summary>
        /// <returns><see langword="true"/> when a command was removed; otherwise, <see langword="false"/>.</returns>
        public bool TryDequeue(out PendingCommand command)
        {
            if (!commands.Reader.TryRead(out command))
                return false;

            Interlocked.Decrement(ref count);
            return true;
        }

        /// <summary>
        /// Discard every pending command while leaving the queue open.
        /// </summary>
        public void Clear()
        {
            while (TryDequeue(out _))
            {
            }
        }

        /// <summary>
        /// Stop command admission and discard every pending command.
        /// </summary>
        public void Complete()
        {
            commands.Writer.TryComplete();
            Clear();
        }

        private bool TryReserve()
        {
            while (true)
            {
                int current = Volatile.Read(ref count);
                if (current >= maximumPendingCommands)
                    return false;

                if (Interlocked.CompareExchange(ref count, current + 1, current) == current)
                    return true;
            }
        }
    }
}
