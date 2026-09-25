using System.Text;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The <c>run</c> kind on the owner's pipe: both ways of naming variables round trip, every field is
/// bounded before anything is resolved, and a reply that says no never carries a value.
/// </summary>
public sealed class ApproverRunProtocolTests
{
    private const string _sentinel = "sk_live_RUN_PROTOCOL_SENTINEL";

    private static RunRequest SetRun() => new()
    {
        Program = OperatingSystem.IsWindows() ? @"C:\tools\npm.cmd" : "/usr/bin/npm",
        Command = ["npm", "run", "migrate"],
        Directory = OperatingSystem.IsWindows() ? @"C:\work\api" : "/work/api",
        Project = "acme-api",
        Profile = "staging",
        Keys = ["DATABASE_URL"],
        Reason = "run the pending migration",
        Exposure = ["env/**"],
        ClientName = "claude-code",
        ClientVersion = "1.2.3",
        ClientLabel = "claude-code",
        Vault = "/vaults/acme.kdbx",
        Session = "session-one",
    };

    private static RunRequest ReferenceRun() => SetRun() with
    {
        Project = null,
        Profile = EnvProfileNames.Default,
        Keys = null,
        References = [new RunReference("DATABASE_URL", "kp://acme-api/dev/DATABASE_URL"), new RunReference("GH", "kp:///personal/github#username")],
    };

    [Fact]
    public void BothModes_RoundTrip()
    {
        foreach (var sent in new[] { SetRun(), ReferenceRun() })
        {
            var frame = ApproverProtocol.Encode(sent);

            Assert.Equal(ApproverMessageKind.Run, ApproverProtocol.KindOf(frame));
            Assert.True(ApproverProtocol.TryDecode(frame, out RunRequest? received));
            Assert.Equal(sent.Program, received.Program);
            Assert.Equal(sent.Command, received.Command);
            Assert.Equal(sent.Directory, received.Directory);
            Assert.Equal(sent.Project, received.Project);
            Assert.Equal(sent.Profile, received.Profile);
            Assert.Equal(sent.Keys, received.Keys);
            Assert.Equal(sent.References, received.References);
            Assert.Equal(sent.Reason, received.Reason);
            Assert.Equal(sent.Exposure, received.Exposure);
            Assert.Equal(sent.ClientLabel, received.ClientLabel);
            Assert.Equal(sent.Session, received.Session);
        }
    }

    [Fact]
    public void AReleasedReply_RoundTrips_WithItsValues()
    {
        var reply = new RunReply(EnvResolved.Released("acme-api", [new EnvVariable("DATABASE_URL", _sentinel)], "staging"), AuditMethod.Prompt, "a person approved this one run")
        {
            GrantedSeconds = 900,
            Entries = ["env/acme-api/staging/DATABASE_URL"],
            Session = "session-one",
        };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out RunReply? decoded));
        Assert.Equal(EnvOutcome.Resolved, decoded.Set.Outcome);
        Assert.Equal(_sentinel, Assert.Single(decoded.Set.Variables).Value);
        Assert.Equal(900, decoded.GrantedSeconds);
        Assert.Equal(reply.Entries, decoded.Entries);
        Assert.Equal(AuditMethod.Prompt, decoded.Method);
    }

    [Fact]
    public void AReplyWithVariablesOnARefusal_IsRejected()
    {
        const string frame = """{"v":2,"kind":"run","project":"p","profile":"dev","outcome":7,"reason":"no","method":5,"granted_seconds":0,"entries":[],"problems":[],"variables":[{"key":"A","value":"leaked"}]}""";

        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out RunReply? _));
    }

    [Fact]
    public void AReplyWithAnUndefinedMethod_IsRejected()
    {
        const string frame = """{"v":2,"kind":"run","project":"p","profile":"dev","outcome":7,"reason":"no","method":999,"granted_seconds":0,"entries":[],"problems":[]}""";

        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out RunReply? _));
    }

    [Fact]
    public void OversizedFields_AreRejected()
    {
        foreach (var oversized in new[]
        {
            SetRun() with { Command = [.. Enumerable.Repeat("x", 257)] },
            SetRun() with { Command = ["npm", new string('x', 4097)] },
            SetRun() with { Command = [] },
            SetRun() with { Keys = [.. Enumerable.Range(0, 33).Select(i => $"K{i}")] },
            ReferenceRun() with { References = [.. Enumerable.Range(0, 33).Select(i => new RunReference($"K{i}", "kp://p/dev/K"))] },
            ReferenceRun() with { References = [new RunReference("K", "kp://" + new string('p', 2100))] },
            SetRun() with { Reason = new string('r', 2001) },
            SetRun() with { Exposure = [.. Enumerable.Repeat("env/**", 65)] },
        })
        {
            Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(oversized), out RunRequest? _));
        }
    }

    [Fact]
    public void AnOversizedReply_IsTooLarge_AndUndeliverable()
    {
        var huge = EnvResolved.Released("acme-api", [new EnvVariable("BLOB", new string('v', MessageFramer.MaximumPayloadBytes))]);
        var frame = ApproverProtocol.Encode(new RunReply(huge, AuditMethod.Prompt, "approved"));

        Assert.True(frame.Length <= MessageFramer.MaximumPayloadBytes);
        Assert.True(ApproverProtocol.TryDecode(frame, out RunReply? decoded));
        Assert.Equal(EnvOutcome.TooLarge, decoded.Set.Outcome);
        Assert.Equal(AuditMethod.Undeliverable, decoded.Method);
        Assert.Empty(decoded.Set.Variables);
    }

    [Fact]
    public void RunReply_ToString_HasNoValue()
    {
        var reply = new RunReply(EnvResolved.Released("acme-api", [new EnvVariable("K", _sentinel)]), AuditMethod.Prompt, "ok");

        Assert.DoesNotContain(_sentinel, reply.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_sentinel, $"{reply}", StringComparison.Ordinal);
    }

    [Fact]
    public void AttachClient_RoundTrips_AndAnOldAttachStillDecodes()
    {
        var sent = new AttachRequest("/vaults/acme.kdbx") { Client = new AttachClient("claude-code", "1.2.3", "cc") };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(sent), out AttachRequest? received));
        Assert.Equal(sent.Client, received.Client);

        Assert.True(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes("""{"v":2,"kind":"attach","vault":"/v.kdbx"}"""), out AttachRequest? old));
        Assert.Null(old.Client);
    }

    [Fact]
    public void AnAttachWithAnOverlongIdentity_IsRejected()
    {
        var frame = ApproverProtocol.Encode(new AttachRequest("/v.kdbx") { Client = new AttachClient(new string('n', 65), null, null) });

        Assert.False(ApproverProtocol.TryDecode(frame, out AttachRequest? _));
    }
}
