using System;
using System.Globalization;

namespace Org.BouncyCastle.Utilities
{
    internal static class Platform
    {
        internal static string GetTypeName(object obj)
        {
            return GetTypeName(obj?.GetType());
        }

        internal static string GetTypeName(Type t)
        {
            return t?.FullName ?? "null";
        }
    }
}
