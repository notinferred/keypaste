using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Keypaste.Core.Browser;

/// <summary>The default values of keys under <c>HKCU</c>, where a Windows browser looks up its manifests.</summary>
public interface IHostRegistry
{
    /// <summary>Reads a key's default value.</summary>
    /// <param name="key">The key under <c>HKCU</c>.</param>
    /// <returns>The value, or null when the key or the value is absent or unreadable.</returns>
    string? Read(string key);

    /// <summary>Writes a key's default value, making the key.</summary>
    /// <param name="key">The key under <c>HKCU</c>.</param>
    /// <param name="value">The manifest's path.</param>
    /// <returns><see langword="false"/> when it could not be written.</returns>
    bool Write(string key, string value);

    /// <summary>Removes a key with nothing under it; an absent one is already removed.</summary>
    /// <param name="key">The key under <c>HKCU</c>.</param>
    /// <returns><see langword="false"/> when it could not be removed.</returns>
    bool Delete(string key);
}

/// <summary>The current user's registry.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryHostKeys : IHostRegistry
{
    /// <inheritdoc/>
    public string? Read(string key)
    {
        try
        {
            using var opened = Registry.CurrentUser.OpenSubKey(key);
            return opened?.GetValue(null) as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public bool Write(string key, string value)
    {
        try
        {
            using var opened = Registry.CurrentUser.CreateSubKey(key, writable: true);
            opened.SetValue(null, value, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Delete(string key)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKey(key, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            return false;
        }
    }
}
