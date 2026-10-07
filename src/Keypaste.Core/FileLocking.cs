namespace Keypaste.Core;

/// <summary>Whether this process's exclusive file handles exclude anybody.</summary>
/// <remarks>
/// Off Windows, .NET grants <see cref="FileShare.None"/> through an advisory lock that the
/// <c>System.IO.DisableFileLocking</c> switch, or <c>DOTNET_SYSTEM_IO_DISABLEFILELOCKING</c>, turns
/// off. A vault claim or the audit log's lock taken then would be held by every process at once, so
/// they refuse instead. Windows share modes are not affected.
/// </remarks>
internal static class FileLocking
{
    internal const string SwitchName = "System.IO.DisableFileLocking";
    internal const string VariableName = "DOTNET_SYSTEM_IO_DISABLEFILELOCKING";

    /// <summary>Whether exclusive handles are advisory locks this process has turned off.</summary>
    public static bool IsDisabled { get; } = Disabled(
        OperatingSystem.IsWindows(),
        AppContext.TryGetSwitch(SwitchName, out var on) ? on : null,
        Environment.GetEnvironmentVariable(VariableName));

    /// <summary>Why a lock is refused while <see cref="IsDisabled"/>.</summary>
    public const string Refusal =
        $"file locking is turned off in this process ({VariableName} or {SwitchName}), so keypaste cannot keep other processes out. Unset it and try again.";

    /// <summary>The runtime's own reading: a switch that is set wins, then the variable as 1 or true.</summary>
    internal static bool Disabled(bool windows, bool? switchValue, string? variable) =>
        !windows && (switchValue ?? (variable is { } value
            && (value.Trim() == "1" || string.Equals(value.Trim(), bool.TrueString, StringComparison.OrdinalIgnoreCase))));
}
