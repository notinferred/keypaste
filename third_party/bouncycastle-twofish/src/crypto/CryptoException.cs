using System;
using System.Runtime.Serialization;

namespace Org.BouncyCastle.Crypto
{
    [Serializable]
    internal class CryptoException
        : Exception
    {
        public CryptoException(string message)
            : base(message)
        {
        }
    }
}
