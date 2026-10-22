using System;
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
using System.Buffers;
#endif

using Org.BouncyCastle.Utilities;

namespace Org.BouncyCastle.Crypto.Parameters
{
    /// <summary>Base class for symmetric key parameters.</summary>
    internal class KeyParameter
        : ICipherParameters
    {
        private readonly byte[] m_key;

        /// <summary>Initializes a new instance of <see cref="KeyParameter"/>.</summary>
        /// <param name="key">The byte array containing the key material.</param>
        public KeyParameter(byte[] key)
        {
            m_key = Arrays.CopyBuffer(key);
        }

        /// <summary>Gets the key material.</summary>
        /// <returns>A copy of the key material as a byte array.</returns>
        public byte[] GetKey() => Arrays.InternalCopyBuffer(m_key);

        // keypaste addition: zeroes the copy of the key this holds. See third_party/bouncycastle-twofish/UPSTREAM.md.
        internal void Clear() => Array.Clear(m_key, 0, m_key.Length);
    }
}
