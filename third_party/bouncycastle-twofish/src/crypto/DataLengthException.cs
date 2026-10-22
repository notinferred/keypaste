using System;
using System.Runtime.Serialization;

namespace Org.BouncyCastle.Crypto
{
    /// <summary>This exception is thrown if a buffer that is meant to have output copied into it turns out to be too
    /// short, or if we've been given insufficient input.</summary>
    /// <remarks>
    /// In general this exception will get thrown rather than an <see cref="IndexOutOfRangeException"/>.
    /// </remarks>
    [Serializable]
    internal class DataLengthException
        : CryptoException
    {
        public DataLengthException(string message)
            : base(message)
        {
        }
    }
}
