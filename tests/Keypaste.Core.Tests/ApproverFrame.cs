using System.Globalization;
using System.Text;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Tests;

/// <summary>Hand-written approver frames, carrying the wire version this build speaks unless a test names another.</summary>
internal static class ApproverFrame
{
    /// <summary>The bytes of the JSON object <paramref name="json"/> with <paramref name="version"/> as its first member, <c>v</c>.</summary>
    /// <param name="json">An object that names no version; it may be left unclosed for the caller to finish.</param>
    /// <param name="version">The version the frame claims.</param>
    /// <remarks>A row written with a literal version would be refused for that version once the protocol moves on, not for the defect it names.</remarks>
    public static byte[] Versioned(string json, int version = ApproverProtocol.Version)
    {
        if (!json.StartsWith('{'))
        {
            throw new ArgumentException("A frame is a JSON object.", nameof(json));
        }

        var members = json[1..];
        var separator = members.StartsWith('}') ? string.Empty : ",";

        return Encoding.UTF8.GetBytes("{\"v\":" + version.ToString(CultureInfo.InvariantCulture) + separator + members);
    }
}
