using System;
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
using System.Buffers.Binary;
#endif
using System.Diagnostics;
#if NETSTANDARD1_0_OR_GREATER || NETCOREAPP1_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace Org.BouncyCastle.Crypto.Utilities
{
    internal static class Pack
    {
        internal static uint LE_To_UInt32(byte[] bs, int off)
        {
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
            return BinaryPrimitives.ReadUInt32LittleEndian(bs.AsSpan(off));
#else
            return bs[off]
                | (uint)bs[off + 1] << 8
                | (uint)bs[off + 2] << 16
                | (uint)bs[off + 3] << 24;
#endif
        }

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint LE_To_UInt32(ReadOnlySpan<byte> bs)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(bs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void UInt32_To_LE(uint n, Span<byte> bs)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bs, n);
        }
#endif
    }
}
