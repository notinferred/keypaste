using System;
using System.Runtime.Serialization;

namespace Org.BouncyCastle.Crypto
{
    [Serializable]
    internal class OutputLengthException
        : DataLengthException
    {
        public OutputLengthException(string message)
            : base(message)
        {
        }
    }
}
