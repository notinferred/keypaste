using System;
using System.Runtime.Serialization;

namespace Org.BouncyCastle.Crypto
{
    /// <summary>This exception is thrown whenever we find something we don't expect in a message.</summary>
    [Serializable]
    internal class InvalidCipherTextException
        : CryptoException
    {
        public InvalidCipherTextException(string message)
            : base(message)
        {
        }
    }
}
