using System.Runtime.InteropServices;

namespace Keypaste.Core.Ipc;

/// <summary>macOS's <c>audit_token_t</c> for a process, which names it by its id and that id's generation, so a reused id does not match it.</summary>
/// <remarks>Opaque, as Apple asks: only the system's own functions read it.</remarks>
[StructLayout(LayoutKind.Sequential, Size = 32)]
internal readonly struct MacAuditToken;
