using System.Text;
using Keypaste.Core.Agents;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// <c>clients.toml</c> as this computer's ceilings: a strict file of <c>[[client]]</c> rows, read whole
/// or refused whole, own rows else the <c>*</c> rows, re-read whenever its bytes change, and never
/// written larger than it is read.
/// </summary>
public sealed class AccessCapsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-caps-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string FilePath => Path.Combine(_directory, "clients.toml");

    private static bool Parse(string text, out AccessCaps? caps, out string problem) =>
        AccessCaps.TryParse(Encoding.UTF8.GetBytes(text), out caps, out problem);

    /// <summary>A mode ceiling for agent <paramref name="i"/>, or, given a project, one for that project under a 64-character label.</summary>
    private static CapsRow Ceiling(int i, string? project = null) => project is null
        ? new CapsRow { Label = $"agent-{i}", Mode = AgentMode.Ask }
        : new CapsRow { Label = $"{i:D64}", Project = Scope(project), Mode = AgentMode.AllowForAWhile };

    /// <summary>Ceilings of many rows, read from the text each row is written as rather than added one at a time.</summary>
    private static AccessCaps Many(IEnumerable<CapsRow> rows)
    {
        var header = AccessCaps.Empty.Format();

        Assert.True(Parse(header + string.Concat(rows.Select(row => Caps(row).Format()[header.Length..])), out var caps, out var problem), problem);
        return caps!;
    }

    [Fact]
    public void AFile_RoundTrips()
    {
        var written = Caps(
            new CapsRow { Label = "claude-code", Mode = AgentMode.AllowForAWhile, Values = false, Run = false, See = See("tag:env:billing", "services/**") },
            new CapsRow { Label = "claude-code", Project = Scope("billing:staging"), Mode = AgentMode.Ask },
            new CapsRow { Label = "cursor", Run = true, Values = true },
            new CapsRow { Label = AgentNames.Any, See = See() });

        Assert.True(Parse(written.Format(), out var read, out var problem), problem);
        Assert.Equal(written.Rows, read!.Rows);
        Assert.Contains("see = [\"tag:env:billing\", \"services/**\"]", written.Format(), StringComparison.Ordinal);
        Assert.Contains("project = \"billing:staging\"", written.Format(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[[client]]\nlabel = \"a\"\npolicy = \"ask\"\n", 3)]
    [InlineData("[[client]]\nlabel = \"a\"\nmode = \"ask\"\ncolour = \"red\"\n", 4)]
    [InlineData("[[clients]]\nlabel = \"a\"\nmode = \"ask\"\n", 1)]
    [InlineData("[[client]]\nlabel = \"a\"\nmode = \"sometimes\"\n", 3)]
    [InlineData("[[client]]\nlabel = \"a\"\nvalues = \"off\"\n", 3)]
    [InlineData("[[client]]\nlabel = \"a\"\nrun = \"no\"\n", 3)]
    [InlineData("[[client]]\nlabel = \"a\"\nsee = \"tag:env:*\"\n", 3)]
    [InlineData("[[client]]\nlabel = \"a\"\nsee = [\"tag:env:\"]\n", 3)]
    [InlineData("[[client]]\nlabel = \"a\"\nproject = \"billing\"\nvalues = \"no\"\nmode = \"ask\"\n", 1)]
    [InlineData("[[client]]\nlabel = \"a\"\nproject = \"billing\"\n", 1)]
    [InlineData("[[client]]\nlabel = \"a\"\nproject = \"a:b:c\"\nmode = \"ask\"\n", 3)]
    [InlineData("[[client]]\nlabel = \"a\"\n", 1)]
    [InlineData("[[client]]\nlabel = \"a/b\"\nmode = \"ask\"\n", 2)]
    [InlineData("[[client]]\nmode = \"ask\"\n", 1)]
    [InlineData("[[client]]\nlabel = \"a\"\nmode = \"ask\"\n[[client]]\nlabel = \"a\"\nrun = \"off\"\n", 4)]
    [InlineData("label = \"a\"\n", 1)]
    public void AMalformedFile_IsRefusedWhole_NamingItsLine(string text, int line)
    {
        Assert.False(Parse(text, out var caps, out var problem));
        Assert.Null(caps);
        Assert.StartsWith($"line {line}: ", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEarlierPolicyForm_IsMalformed()
    {
        Assert.False(Parse("[[client]]\nlabel = \"claude-code\"\npolicy = \"inject-only\"\n", out _, out var problem));

        Assert.Contains("'policy' is not a setting", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoRowsForDifferentProjects_AreAllowed() =>
        Assert.True(Parse(
            "[[client]]\nlabel = \"a\"\nproject = \"billing\"\nmode = \"ask\"\n[[client]]\nlabel = \"a\"\nproject = \"billing:staging\"\nmode = \"automatic\"\n",
            out _,
            out _));

    [Fact]
    public void ANamedAgent_IsHeldToItsOwnRows_ElseTheAnyRows()
    {
        var caps = Caps(
            new CapsRow { Label = "claude-code", Mode = AgentMode.Ask },
            new CapsRow { Label = AgentNames.Any, Values = false });

        Assert.Equal(AgentMode.Ask, caps.For("claude-code").ModeFor(null, null));
        Assert.True(caps.For("claude-code").Values);

        foreach (var label in new string?[] { "cursor", null, string.Empty, AgentNames.Any, "a/b" })
        {
            Assert.False(caps.For(label).Values);
            Assert.Equal(AgentMode.Automatic, caps.For(label).ModeFor(null, null));
        }
    }

    [Fact]
    public void AModeCeiling_IsTheEnvironmentsRow_ElseTheProjectsRow_ElseTheAgentsRow()
    {
        var ceiling = Caps(
            new CapsRow { Label = "a", Mode = AgentMode.AllowForAWhile },
            new CapsRow { Label = "a", Project = Scope("billing"), Mode = AgentMode.Ask },
            new CapsRow { Label = "a", Project = Scope("billing:staging"), Mode = AgentMode.Automatic }).For("a");

        Assert.Equal(AgentMode.Automatic, ceiling.ModeFor("billing", "staging"));
        Assert.Equal(AgentMode.Ask, ceiling.ModeFor("billing", "dev"));
        Assert.Equal(AgentMode.Ask, ceiling.ModeFor("billing", null));
        Assert.Equal(AgentMode.AllowForAWhile, ceiling.ModeFor("other", "dev"));
        Assert.Equal(AgentMode.AllowForAWhile, ceiling.ModeFor(null, null));
        Assert.Equal(AgentMode.Automatic, AccessCeiling.None.ModeFor("billing", "dev"));
    }

    [Fact]
    public void With_ReplacesTheRowForTheSameScope_AndWithoutRemovesIt()
    {
        var caps = Caps(
            new CapsRow { Label = "a", Mode = AgentMode.Ask },
            new CapsRow { Label = "a", Project = Scope("billing"), Mode = AgentMode.Ask })
            .With(new CapsRow { Label = "a", Values = false });

        Assert.Equal(2, caps.Rows.Count);
        Assert.Null(caps.For("a").Rows[0].Mode);
        Assert.False(caps.For("a").Values);

        Assert.Single(caps.Without("a", Scope("billing")).Rows);
        Assert.Single(caps.Without("a", null).Rows);
    }

    [Fact]
    public void With_RefusesARowTheFileCannotHold()
    {
        CapsRow[] rows =
        [
            new CapsRow { Label = "a/b", Mode = AgentMode.Ask },
            new CapsRow { Label = "a" },
            new CapsRow { Label = "a", Project = Scope("billing") },
            new CapsRow { Label = "a", Project = Scope("billing"), Mode = AgentMode.Ask, Run = false },
        ];

        foreach (var row in rows)
        {
            Assert.Throws<ArgumentException>(() => AccessCaps.Empty.With(row));
        }
    }

    /// <summary>A file holds the most rows it may, and those read back; a row more is refused before anything is written.</summary>
    [Fact]
    public void AFile_HoldsTheMostRows_AndARowMoreIsRefused()
    {
        var full = Many(Enumerable.Range(0, AccessCaps.MaximumRows).Select(i => Ceiling(i)));

        Assert.True(AccessCaps.TrySave(FilePath, full, out var error), error);
        Assert.True(new AccessCapsSource(FilePath).TryCurrent(out var read, out var problem), problem);
        Assert.Equal(AccessCaps.MaximumRows, read.Rows.Count);

        var refused = Assert.Throws<ArgumentException>(() => full.With(Ceiling(AccessCaps.MaximumRows)));
        Assert.Contains($"at most {AccessCaps.MaximumRows} rows", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rows long enough to fill the file's size before its row count are held as far as they fit, and one
    /// more is refused rather than written as a file every agent request would then be refused for.
    /// </summary>
    [Fact]
    public void AFile_HoldsAsManyLongRowsAsItsSizeAllows_AndOneMoreIsRefused()
    {
        var project = new string('p', 128);
        var header = AccessCaps.Empty.Format().Length;
        var fits = (AccessCaps.MaximumBytes - header) / (Caps(Ceiling(0, project)).Format().Length - header);
        var near = Many(Enumerable.Range(0, fits).Select(i => Ceiling(i, project)));

        Assert.InRange(fits, 1, AccessCaps.MaximumRows - 1);
        Assert.True(AccessCaps.TrySave(FilePath, near, out var error), error);
        Assert.True(new AccessCapsSource(FilePath).TryCurrent(out var read, out var problem), problem);
        Assert.Equal(fits, read.Rows.Count);

        var refused = Assert.Throws<ArgumentException>(() => near.With(Ceiling(fits, project)));
        Assert.Contains($"at most {AccessCaps.MaximumBytes / 1024} KiB", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFile_IsSavedAndLoaded_AndNoFileIsNoCeiling()
    {
        Assert.True(AccessCaps.TryLoad(FilePath, out var none, out _));
        Assert.Same(AccessCaps.Empty, none);

        var caps = Caps(new CapsRow { Label = "a", Values = false });

        Assert.True(AccessCaps.TrySave(FilePath, caps, out var error), error);
        Assert.True(AccessCaps.TryLoad(FilePath, out var loaded, out var problem), problem);
        Assert.Equal(caps.Rows, loaded!.Rows);
    }

    /// <summary>A row holds a limit on Automatic, its end in UTC, and reads it back; a malformed limit, or a project row with no mode, is refused naming its line.</summary>
    [Fact]
    public void ARowsLimit_RoundTrips_AndAMalformedOneIsRefused()
    {
        var until = new DateTimeOffset(2026, 12, 31, 17, 0, 0, TimeSpan.Zero);
        var caps = Caps(
            new CapsRow { Label = "a", Limit = new AccessLimit { PerHour = 5 } },
            new CapsRow { Label = "a", Project = Scope("billing:dev"), Mode = AgentMode.Automatic, Limit = new AccessLimit { Until = until } });

        Assert.True(Parse(caps.Format(), out var read, out var problem), problem);
        Assert.Equal(caps.Rows, read!.Rows);
        Assert.Contains("until = \"2026-12-31T17:00:00Z\"", caps.Format(), StringComparison.Ordinal);

        Assert.False(Parse("[[client]]\nlabel = \"a\"\nper_hour = 0\n", out _, out problem));
        Assert.StartsWith("line 3: per_hour", problem, StringComparison.Ordinal);
        Assert.False(Parse("[[client]]\nlabel = \"a\"\nuntil = \"tomorrow\"\n", out _, out problem));
        Assert.StartsWith("line 3: until", problem, StringComparison.Ordinal);
        Assert.False(Parse("[[client]]\nlabel = \"a\"\nproject = \"billing\"\nper_hour = 5\n", out _, out problem));
        Assert.Contains("holds its mode", problem, StringComparison.Ordinal);
    }

    /// <summary>A change starts from what the file holds at that moment, so a row written since keypaste last read it stays.</summary>
    [Fact]
    public void AChange_StartsFromWhatTheFileHoldsNow()
    {
        Assert.True(AccessCaps.TrySave(FilePath, Caps(new CapsRow { Label = "a", Values = false }), out var error), error);
        File.AppendAllText(FilePath, "[[client]]\nlabel = \"b\"\nrun = \"off\"\n");

        var changed = AccessCaps.TryChange(FilePath, caps => caps.Without("a", null), out var problem);

        Assert.Equal(CapsChange.Written, changed);
        Assert.True(AccessCaps.TryLoad(FilePath, out var loaded, out problem), problem);
        Assert.Equal(new CapsRow { Label = "b", Run = false }, Assert.Single(loaded.Rows));
        Assert.Equal(CapsChange.Unchanged, AccessCaps.TryChange(FilePath, caps => caps, out _));
        Assert.Equal(CapsChange.Declined, AccessCaps.TryChange(FilePath, _ => null, out _));
    }

    /// <summary>While another keypaste process is changing the file, a change waits a moment for it and then writes nothing, saying why.</summary>
    [Fact]
    public void AChange_WritesNothingWhileAnotherKeypasteProcessIsChangingTheFile()
    {
        Assert.True(AccessCaps.TrySave(FilePath, Caps(new CapsRow { Label = "a", Values = false }), out var error), error);

        using (new FileStream(FilePath + AccessCaps.LockSuffix, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            var changed = AccessCaps.TryChange(FilePath, caps => caps.Without("a", null), out var problem);

            Assert.Equal(CapsChange.Unwritten, changed);
            Assert.Contains("held by another keypaste process", problem, StringComparison.Ordinal);
        }

        Assert.True(AccessCaps.TryLoad(FilePath, out var loaded, out var read), read);
        Assert.Single(loaded.Rows);
        Assert.Equal(CapsChange.Written, AccessCaps.TryChange(FilePath, caps => caps.Without("a", null), out _));
    }

    /// <summary>
    /// A reset sets a file keypaste cannot parse aside beside it and starts an empty one, a later reset sets
    /// its file aside under the next free name, never replacing a copy kept before, and a file that reads
    /// has nothing to reset.
    /// </summary>
    [Fact]
    public void AReset_SetsAMalformedFileAside_UnderTheNextFreeName_AndStartsAnEmptyOne()
    {
        const string Malformed = "[[client]]\nlabel = \"x\"\npolicy = \"ask\"\n";
        const string Later = "[[client]]\nlabel = \"y\"\npolicy = \"ask\"\n";
        File.WriteAllText(FilePath, Malformed);

        Assert.Equal(CapsFile.Malformed, AccessCaps.Probe(FilePath, out var problem));
        Assert.Contains("policy", problem, StringComparison.Ordinal);
        Assert.Equal(CapsChange.Written, AccessCaps.TryReset(FilePath, out var kept, out problem));
        Assert.Equal(FilePath + AccessCaps.KeptSuffix, kept);
        Assert.Equal(Malformed, File.ReadAllText(kept));
        Assert.True(AccessCaps.TryLoad(FilePath, out var reset, out problem), problem);
        Assert.Empty(reset.Rows);
        Assert.Equal(CapsChange.Unchanged, AccessCaps.TryReset(FilePath, out _, out _));

        File.WriteAllText(FilePath, Later);
        Assert.Equal(CapsChange.Written, AccessCaps.TryReset(FilePath, out var again, out problem));

        Assert.Equal(FilePath + AccessCaps.KeptSuffix + "-2", again);
        Assert.Equal(Malformed, File.ReadAllText(kept));
        Assert.Equal(Later, File.ReadAllText(again));
    }

    /// <summary>What could not be read at all is not known to be malformed, so a reset leaves it as it is.</summary>
    [Fact]
    public void AReset_LeavesWhatCouldNotBeReadAsItIs()
    {
        Directory.CreateDirectory(FilePath);

        Assert.Equal(CapsFile.Unreadable, AccessCaps.Probe(FilePath, out var problem));
        Assert.StartsWith("it could not be read", problem, StringComparison.Ordinal);
        Assert.Equal(CapsChange.Unreadable, AccessCaps.TryReset(FilePath, out _, out _));
        Assert.True(Directory.Exists(FilePath));
        Assert.False(Path.Exists(FilePath + AccessCaps.KeptSuffix));
    }

    [Fact]
    public void TheSource_AppliesAnEditAtTheNextRead_AndRefusesAFileItCannotUse()
    {
        var source = new AccessCapsSource(FilePath);

        Assert.True(source.TryCurrent(out var caps, out _));
        Assert.Empty(caps.Rows);

        File.WriteAllText(FilePath, "[[client]]\nlabel = \"a\"\nvalues = \"no\"\n");
        Assert.True(source.TryCurrent(out caps, out _));
        Assert.False(caps.For("a").Values);

        File.WriteAllText(FilePath, "[[client]]\nlabel = \"a\"\npolicy = \"ask\"\n");
        Assert.False(source.TryCurrent(out caps, out var problem));
        Assert.Null(caps);
        Assert.Contains("line 3", problem, StringComparison.Ordinal);
        Assert.False(source.TryCurrent(out _, out _));

        File.Delete(FilePath);
        Directory.CreateDirectory(FilePath);
        Assert.False(source.TryCurrent(out _, out problem));
        Assert.Contains(FilePath, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AHandEditedCeiling_OnlyEverTakesAway()
    {
        var records = Own(Automatic with { Values = false, Projects = [Row("billing", AgentMode.Ask)] });
        var caps = Caps(
            new CapsRow { Label = "a", Mode = AgentMode.Automatic, Values = true, Run = true, See = See("**") },
            new CapsRow { Label = "a", Project = Scope("billing"), Mode = AgentMode.Automatic });

        var access = EffectiveAccess.Create("a", records, caps);

        Assert.False(access.Values);
        Assert.False(access.Run);
        Assert.Equal(AgentMode.Ask, access.Item(["env:billing"], DateTimeOffset.UtcNow).Mode);
        Assert.Equal(ExposureReach.ProjectFields, access.Reach(See("**"), new EntryName("a", "b"), ["env:billing"]));
        Assert.Equal(ExposureReach.None, access.Reach(See("**"), new EntryName("a", "b"), []));
    }
}
