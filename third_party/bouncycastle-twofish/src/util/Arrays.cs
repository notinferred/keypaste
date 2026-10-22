using System;
using System.Runtime.CompilerServices;
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
using System.Runtime.InteropServices;
using System.Security.Cryptography;
#endif
using System.Text;

namespace Org.BouncyCastle.Utilities
{
    /// <summary> General array utilities.</summary>
    internal static class Arrays
    {
        // TODO[api] Redundant with generic version
        public static void Fill(byte[] buf, byte b) => Fill<byte>(buf, b);

        public static void Fill<T>(T[] ts, T t)
        {
#if NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
            Array.Fill(ts, t);
#else
            ValidateBuffer(ts);
            for (int i = 0; i < ts.Length; ++i)
            {
                ts[i] = t;
            }
#endif
        }

        public static T[] CopyBuffer<T>(T[] buf)
        {
            ValidateBuffer(buf);
            return InternalCopyBuffer(buf);
        }

        public static void CopyBufferToSegment<T>(T[] srcBuf, T[] dstBuf, int dstOff, int dstLen)
        {
            ValidateBuffer(srcBuf);
            ValidateSegment(dstBuf, dstOff, dstLen);
            InternalCopyBufferToSegment(srcBuf, dstBuf, dstOff, dstLen);
        }

        internal static T[] InternalCopyBuffer<T>(T[] buf)
        {
#if NET5_0_OR_GREATER
            T[] result = GC.AllocateUninitializedArray<T>(buf.Length);
#else
            T[] result = new T[buf.Length];
#endif
            Array.Copy(buf, 0, result, 0, buf.Length);
            return result;
        }

        internal static void InternalCopyBufferToSegment<T>(T[] srcBuf, T[] dstBuf, int dstOff, int dstLen)
        {
            if (srcBuf.Length != dstLen)
                throw new ArgumentOutOfRangeException(nameof(dstLen));

            Array.Copy(srcBuf, 0, dstBuf, dstOff, dstLen);
        }

        public static void ValidateBuffer<T>(T[] buf)
        {
            if (buf == null)
                throw new ArgumentNullException(nameof(buf));
        }

        public static void ValidateSegment<T>(T[] buf, int off, int len)
        {
            if (buf == null)
                throw new ArgumentNullException(nameof(buf));
            int available = buf.Length - off;
            if ((off | available) < 0)
                throw new ArgumentOutOfRangeException(nameof(off));
            int remaining = available - len;
            if ((len | remaining) < 0)
                throw new ArgumentOutOfRangeException(nameof(len));
        }
    }
}
