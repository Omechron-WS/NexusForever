using System;
using System.Globalization;

namespace NexusForever.Database
{
    /// <summary>
    /// Tracks dirty enum-mask bits and their individual mutation versions.
    /// </summary>
    /// <typeparam name="TMask">Enum type used for the dirty mask.</typeparam>
    public sealed class VersionedSaveMask<TMask> where TMask : struct, Enum
    {
        private const int MaximumBitCount = sizeof(ulong) * 8;

        private readonly object syncRoot = new();
        private readonly ulong[] versions = new ulong[MaximumBitCount];
        private ulong mask;

        /// <summary>
        /// Create an empty versioned save mask.
        /// </summary>
        public VersionedSaveMask()
        {
        }

        /// <summary>
        /// Create a versioned save mask with the supplied initial dirty bits.
        /// </summary>
        /// <param name="initialMask">Initial dirty bits.</param>
        public VersionedSaveMask(TMask initialMask)
        {
            mask = ToUInt64(initialMask);
        }

        /// <summary>
        /// Return the currently dirty bits.
        /// </summary>
        public TMask Current
        {
            get
            {
                lock (syncRoot)
                    return FromUInt64(mask);
            }
        }

        /// <summary>
        /// Return whether any dirty bits are currently set.
        /// </summary>
        public bool IsDirty
        {
            get
            {
                lock (syncRoot)
                    return mask != 0ul;
            }
        }

        /// <summary>
        /// Mark the supplied bits as dirty and advance each bit's mutation version.
        /// </summary>
        /// <param name="dirtyMask">Bits to mark as dirty.</param>
        public void Mark(TMask dirtyMask)
        {
            ulong dirtyBits = ToUInt64(dirtyMask);
            lock (syncRoot)
            {
                AdvanceVersions(dirtyBits);
                mask |= dirtyBits;
            }
        }

        /// <summary>
        /// Clear the supplied bits and advance each bit's mutation version.
        /// </summary>
        /// <param name="clearMask">Bits to clear.</param>
        public void Clear(TMask clearMask)
        {
            ulong clearBits = ToUInt64(clearMask);
            lock (syncRoot)
            {
                AdvanceVersions(clearBits);
                mask &= ~clearBits;
            }
        }

        /// <summary>
        /// Capture the current dirty bits and their individual mutation versions.
        /// </summary>
        /// <returns>An immutable snapshot suitable for post-commit acknowledgement.</returns>
        public VersionedSaveMaskSnapshot<TMask> Capture()
        {
            lock (syncRoot)
                return new VersionedSaveMaskSnapshot<TMask>(this, mask, (ulong[])versions.Clone());
        }

        /// <summary>
        /// Clear captured bits that have not changed since the supplied snapshot was taken.
        /// </summary>
        /// <param name="snapshot">Snapshot captured by this save mask.</param>
        public void Acknowledge(VersionedSaveMaskSnapshot<TMask> snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (!ReferenceEquals(snapshot.Owner, this))
                throw new ArgumentException("The snapshot belongs to a different save mask.", nameof(snapshot));

            lock (syncRoot)
            {
                ulong capturedBits = snapshot.Bits;
                for (int bitIndex = 0; bitIndex < MaximumBitCount && capturedBits != 0ul; bitIndex++, capturedBits >>= 1)
                {
                    if ((capturedBits & 1ul) == 0ul || versions[bitIndex] != snapshot.Versions[bitIndex])
                        continue;

                    mask &= ~(1ul << bitIndex);
                }
            }
        }

        private void AdvanceVersions(ulong bits)
        {
            for (int bitIndex = 0; bitIndex < MaximumBitCount && bits != 0ul; bitIndex++, bits >>= 1)
            {
                if ((bits & 1ul) != 0ul)
                    versions[bitIndex]++;
            }
        }

        private static ulong ToUInt64(TMask value)
        {
            return Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TMask))) switch
            {
                TypeCode.SByte  => unchecked((byte)Convert.ToSByte(value, CultureInfo.InvariantCulture)),
                TypeCode.Byte   => Convert.ToByte(value, CultureInfo.InvariantCulture),
                TypeCode.Int16  => unchecked((ushort)Convert.ToInt16(value, CultureInfo.InvariantCulture)),
                TypeCode.UInt16 => Convert.ToUInt16(value, CultureInfo.InvariantCulture),
                TypeCode.Int32  => unchecked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture)),
                TypeCode.UInt32 => Convert.ToUInt32(value, CultureInfo.InvariantCulture),
                TypeCode.Int64  => unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)),
                TypeCode.UInt64 => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
                _               => throw new InvalidOperationException($"Unsupported enum type {typeof(TMask).FullName}.")
            };
        }

        private static TMask FromUInt64(ulong value)
        {
            return (TMask)Enum.ToObject(typeof(TMask), value);
        }
    }

    /// <summary>
    /// Immutable dirty-mask snapshot captured for a pending database save.
    /// </summary>
    /// <typeparam name="TMask">Enum type used for the dirty mask.</typeparam>
    public sealed class VersionedSaveMaskSnapshot<TMask> where TMask : struct, Enum
    {
        internal VersionedSaveMask<TMask> Owner { get; }
        internal ulong Bits { get; }
        internal ulong[] Versions { get; }

        /// <summary>
        /// Return the dirty bits captured by this snapshot.
        /// </summary>
        public TMask Mask => (TMask)Enum.ToObject(typeof(TMask), Bits);

        internal VersionedSaveMaskSnapshot(VersionedSaveMask<TMask> owner, ulong bits, ulong[] versions)
        {
            Owner         = owner;
            Bits          = bits;
            Versions      = versions;
        }
    }
}
