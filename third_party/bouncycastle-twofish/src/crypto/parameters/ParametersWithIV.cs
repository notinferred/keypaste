using System;

using Org.BouncyCastle.Utilities;

namespace Org.BouncyCastle.Crypto.Parameters
{
    /// <summary>
    /// Wrapper class for parameters which include an Initialisation Vector (IV).
    /// </summary>
    internal class ParametersWithIV
        : ICipherParameters
    {
        private readonly ICipherParameters m_parameters;
        private readonly byte[] m_iv;

        /// <summary>
        /// Basic constructor.
        /// </summary>
        /// <param name="parameters">The base parameters (may be null for key reuse).</param>
        /// <param name="iv">The initialization vector.</param>
        public ParametersWithIV(ICipherParameters parameters, byte[] iv)
        {
            // NOTE: 'parameters' may be null to imply key re-use
            m_parameters = parameters;
            m_iv = Arrays.CopyBuffer(iv);
        }

        /// <summary>
        /// Copy the IV into a segment of a destination buffer.
        /// </summary>
        /// <param name="buf">The destination buffer.</param>
        /// <param name="off">The offset into the destination buffer.</param>
        /// <param name="len">The length to copy.</param>
        public void CopyIVTo(byte[] buf, int off, int len) => Arrays.CopyBufferToSegment(m_iv, buf, off, len);

        /// <summary>
        /// Return the length of the IV in bytes.
        /// </summary>
        /// <returns>The length of the IV in bytes.</returns>
        public int IVLength => m_iv.Length;

        /// <summary>
        /// Return the base parameters (e.g., KeyParameter) associated with this IV.
        /// </summary>
        /// <returns>The parameters wrapped by this IV.</returns>
        public ICipherParameters Parameters => m_parameters;
    }
}
