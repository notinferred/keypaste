using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Keypaste.Core.Login;

/// <summary>The string values under a per-user Run key, by name.</summary>
public interface IRunKey
{
    /// <summary>Reads a value.</summary>
    /// <param name="name">The value's name.</param>
    /// <returns>The value, or null when it is absent or unreadable.</returns>
    string? Read(string name);

    /// <summary>Writes a value.</summary>
    /// <param name="name">The value's name.</param>
    /// <param name="value">What it holds.</param>
    /// <returns><see langword="false"/> when it could not be written.</returns>
    bool Write(string name, string value);

    /// <summary>Removes a value; an absent one is already removed.</summary>
    /// <param name="name">The value's name.</param>
    /// <returns><see langword="false"/> when it could not be removed.</returns>
    bool Delete(string name);
}

/// <summary>Windows' own switch for a Run value, which Task Manager and Settings › Apps › Startup turn off without removing the value.</summary>
public interface IStartupApproval
{
    /// <summary>Whether Windows has turned a value off.</summary>
    /// <param name="name">The Run value's name.</param>
    /// <returns><see langword="true"/> when Windows will not start it at login.</returns>
    bool IsDisabled(string name);

    /// <summary>Forgets Windows' switch for a value, so the value starts at login again.</summary>
    /// <param name="name">The Run value's name.</param>
    /// <returns><see langword="false"/> when it could not be cleared.</returns>
    bool Clear(string name);
}

/// <summary>Opens at login on Windows through a value under HKCU's Run key, which needs no elevation.</summary>
public sealed class WindowsRunKey : ILoginItem
{
    /// <summary>The value's name.</summary>
    public const string ValueName = "keypaste";

    private readonly IRunKey _key;
    private readonly IStartupApproval _approval;
    private readonly string _program;

    /// <summary>Initializes the entry for one program.</summary>
    /// <param name="key">Where the value lives.</param>
    /// <param name="approval">Where Windows keeps its own switch for the value.</param>
    /// <param name="program">The executable a login starts.</param>
    public WindowsRunKey(IRunKey key, IStartupApproval approval, string program)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentException.ThrowIfNullOrEmpty(program);

        _key = key;
        _approval = approval;
        _program = program;
        Command = $"\"{program}\" {LoginItems.BackgroundFlag}";
    }

    /// <summary>Gets the command line the value holds.</summary>
    public string Command { get; }

    /// <inheritdoc/>
    public bool IsEnabled =>
        string.Equals(_key.Read(ValueName), Command, StringComparison.Ordinal) && !_approval.IsDisabled(ValueName);

    /// <inheritdoc/>
    /// <remarks>A program path holding a quote cannot be quoted on a Windows command line, so it is refused.</remarks>
    public bool Enable() =>
        !_program.Contains('"', StringComparison.Ordinal) && _key.Write(ValueName, Command) && _approval.Clear(ValueName);

    /// <inheritdoc/>
    public bool Disable() => _key.Delete(ValueName);
}

/// <summary>The current user's registry, under <see cref="RunSubKey"/> unless a test names another key.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryRunKey : IRunKey
{
    /// <summary>The per-user Run key.</summary>
    public const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _subKey;

    /// <summary>Initializes access to one key under HKCU.</summary>
    /// <param name="subKey">The key, <see cref="RunSubKey"/> unless a test names another.</param>
    public RegistryRunKey(string subKey = RunSubKey) => _subKey = subKey;

    /// <inheritdoc/>
    public string? Read(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_subKey);
            return key?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public bool Write(string name, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_subKey, writable: true);
            key.SetValue(name, value, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Delete(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_subKey, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}

/// <summary>The switches Windows keeps under HKCU for the Run key's values, under <see cref="ApprovedSubKey"/> unless a test names another key.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryStartupApproval : IStartupApproval
{
    /// <summary>Where Task Manager and Settings keep the per-user Run values' switches.</summary>
    public const string ApprovedSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string _subKey;

    /// <summary>Initializes access to one key under HKCU.</summary>
    /// <param name="subKey">The key, <see cref="ApprovedSubKey"/> unless a test names another.</param>
    public RegistryStartupApproval(string subKey = ApprovedSubKey) => _subKey = subKey;

    /// <inheritdoc/>
    /// <remarks>The value's first byte is even while the entry is on and odd once it is turned off.</remarks>
    public bool IsDisabled(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_subKey);
            return key?.GetValue(name) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Clear(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_subKey, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}
