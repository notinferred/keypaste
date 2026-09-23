using Keypaste.App.Session;

namespace Keypaste.App.Tests;

/// <summary>
/// A picker that answers whatever the test decided, and counts being asked.
/// </summary>
/// <remarks>
/// <b>The count is the point.</b> Every "nothing was written" assertion about a cancelled picker is
/// vacuous if the picker was never reached — it would pass against code that wrote the vault before
/// opening any dialog. A test asserts <see cref="NewCalls"/> first, and only then that the
/// directory is untouched.
/// </remarks>
internal sealed class FakeVaultFilePicker : IVaultFilePicker
{
    /// <summary>What <see cref="PickNewAsync"/> answers. Null is a cancelled picker.</summary>
    internal string? NewPath { get; set; }

    /// <summary>What <see cref="PickExistingAsync"/> answers. Null is a cancelled picker.</summary>
    internal string? ExistingPath { get; set; }

    /// <summary>What <see cref="PickExportDestinationAsync"/> answers. Null is a cancelled picker.</summary>
    internal string? ExportPath { get; set; }

    /// <summary>How many times the export picker was opened.</summary>
    internal int ExportCalls { get; private set; }

    /// <summary>The name the export picker was last asked to start with.</summary>
    internal string? SuggestedExportName { get; private set; }

    /// <summary>How many times the save picker was opened.</summary>
    internal int NewCalls { get; private set; }

    /// <summary>How many times the open picker was opened.</summary>
    internal int ExistingCalls { get; private set; }

    public Task<string?> PickExistingAsync()
    {
        ExistingCalls++;
        return Task.FromResult(ExistingPath);
    }

    public Task<string?> PickNewAsync()
    {
        NewCalls++;
        return Task.FromResult(NewPath);
    }

    public Task<string?> PickExportDestinationAsync(string suggestedName)
    {
        ExportCalls++;
        SuggestedExportName = suggestedName;
        return Task.FromResult(ExportPath);
    }

    /// <summary>What <see cref="PickKeyfileAsync"/> answers. Null is a cancelled picker.</summary>
    internal string? KeyfilePath { get; set; }

    /// <summary>How many times the keyfile picker was opened.</summary>
    internal int KeyfileCalls { get; private set; }

    public Task<string?> PickKeyfileAsync()
    {
        KeyfileCalls++;
        return Task.FromResult(KeyfilePath);
    }
}
