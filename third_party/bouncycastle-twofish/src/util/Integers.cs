using System;
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
using System.Buffers.Binary;
#endif
#if NETCOREAPP3_0_OR_GREATER
using System.Numerics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Org.BouncyCastle.Utilities
{
    internal static class Integers
    {
        public static int RotateLeft(int i, int distance)
        {
#if NETCOREAPP3_0_OR_GREATER
            return (int)BitOperations.RotateLeft((uint)i, distance);
#else
            return (i << distance) | (int)((uint)i >> -distance);
#endif
        }

        public static int RotateRight(int i, int distance)
        {
#if NETCOREAPP3_0_OR_GREATER
            return (int)BitOperations.RotateRight((uint)i, distance);
#else
            return (int)((uint)i >> distance) | (i << -distance);
#endif
        }
    }
}
