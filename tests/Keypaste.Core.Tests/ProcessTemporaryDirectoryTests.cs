using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The private temporary directory D-0123 puts every save's transacted temporary into.
/// </summary>
/// <remarks>
/// These run in the test process, which redirects once like any other. That is deliberate: the
/// thing being asserted is that redirecting is a one-time, process-wide act, so a test that undid
/// it would be testing something keypaste never does.
/// </remarks>
public sealed class ProcessTemporaryDirectoryTests
{
    [Fact]
    public void ASave_PutsTheProcessesTemporaryPathSomewhereOfItsOwn()
    {
        using var vault = NewSavedVault(out _);
        vault.Save();

        var after = Path.GetTempPath();

        // The end state, not the transition. Redirecting happens once per process and another test
        // in this assembly may already have caused it, which is the property rather than a nuisance.
        Assert.Contains("keypaste-tmp-", after, StringComparison.Ordinal);
        Assert.True(Directory.Exists(after));

        var originalTmp = ProcessTemporaryDirectory.OriginalTemporaryVariables["TMP"];
        Assert.NotNull(originalTmp);
        Assert.NotEqual(
            originalTmp!.TrimEnd(Path.DirectorySeparatorChar),
            after.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void TheDirectoryIsNotSwappedPerSave_SoOtherThreadsNeverSeeItMove()
    {
        using var vault = NewSavedVault(out _);

        vault.Save();
        var afterFirst = Path.GetTempPath();

        vault.Save();
        vault.Save();

        Assert.Equal(afterFirst, Path.GetTempPath());
    }

    [Fact]
    public void WhatAChildMustBeGivenBack_IsTheTemporaryPathKeypasteStartedWith()
    {
        using var vault = NewSavedVault(out _);
        vault.Save();

        var originals = ProcessTemporaryDirectory.OriginalTemporaryVariables;

        Assert.Contains("TMP", originals.Keys);
        Assert.Contains("TEMP", originals.Keys);

        // Whatever they were, they are not where keypaste now writes its own temporaries.
        foreach (var original in originals.Values.Where(value => value is not null))
        {
            Assert.DoesNotContain("keypaste-tmp-", original!, StringComparison.Ordinal);
        }
    }

    private static Vault NewSavedVault(out string path)
    {
        var directory = Directory.CreateTempSubdirectory("keypaste-private-temp-").FullName;
        path = Path.Combine(directory, "vault.kdbx");

        var vault = Vault.Create(path, VaultRoundTripTests.MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "seeded", Password = "seed" });

        // Saved once here so the file exists: FileTransactionEx writes in place when the base file
        // is absent, and never reaches the transacted path these tests are about.
        vault.Save();

        return vault;
    }
}
