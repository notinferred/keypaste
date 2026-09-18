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
}
