using System.Text;
using System.Text.Json;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The wire between the bridge and the approver. It carries a credential on exactly one message, so
/// what it refuses matters more than what it round-trips.
/// </summary>
public sealed class ApproverProtocolTests
{
    private static CredentialRequest Request(string reason = "deploy billing to staging") => new()
    {
        Entry = "env/dev/STRIPE_KEY",
        Field = "password",
        Reason = reason,
        TtlSeconds = 900,
        Exposure = ["env/**"],
        ClientName = "claude-code",
        ClientVersion = "1.2.3",
        ClientLabel = "billing-bot",
    };

    private static CredentialReply Granted(string value = "sk_live_sentinel") => new()
    {
        Decision = AuditDecision.Granted,
        Method = AuditMethod.Prompt,
        Reason = "a person approved this request",
        Entry = "env/dev/STRIPE_KEY",
        TtlSeconds = 300,
        Value = value,
    };

    [Fact]
    public void ACredentialRequestSurvivesTheRoundTrip()
    {
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Request()), out CredentialRequest? decoded));

        // Member by member, not Assert.Equal on the records: a record's generated equality compares
        // the Exposure list by reference, so a whole-object comparison here would be asserting that
        // two lists are the same object rather than that the message survived.
        var original = Request();

        Assert.Equal(original.Entry, decoded.Entry, StringComparer.Ordinal);
        Assert.Equal(original.Field, decoded.Field, StringComparer.Ordinal);
        Assert.Equal(original.Reason, decoded.Reason, StringComparer.Ordinal);
        Assert.Equal(original.TtlSeconds, decoded.TtlSeconds);
        Assert.Equal(original.Exposure, decoded.Exposure);
        Assert.Equal(original.ClientName, decoded.ClientName, StringComparer.Ordinal);
        Assert.Equal(original.ClientVersion, decoded.ClientVersion, StringComparer.Ordinal);
        Assert.Equal(original.ClientLabel, decoded.ClientLabel, StringComparer.Ordinal);
    }

    /// <summary>
    /// The two names travel separately and must never be confused for one another. What the client
    /// asserts about itself is unauthenticated (THREATS.md T-3) and reaches the audit line only;
    /// what the operator wrote in the client's configuration is the one a policy rule keys on.
    /// </summary>
    [Fact]
    public void ACredentialRequest_CarriesTheOperatorsLabel_SeparatelyFromTheAssertedName()
    {
        var request = Request() with { ClientName = "claude-code", ClientLabel = "billing-bot" };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(request), out CredentialRequest? decoded));

        Assert.Equal("claude-code", decoded.ClientName, StringComparer.Ordinal);
        Assert.Equal("billing-bot", decoded.ClientLabel, StringComparer.Ordinal);
        Assert.NotEqual(decoded.ClientName, decoded.ClientLabel);
    }

    /// <summary>
    /// A bridge from before Stage 2.3, or one started without <c>--client-label</c>, sends no label
    /// — and that has to decode rather than fail, because the wire version deliberately did not
    /// change for one optional field. An absent label matches no rule, so the request reaches a
    /// person: the same fallback a malformed policy file produces.
    /// </summary>
    [Fact]
    public void ARequestFromABridgeWithNoLabel_DecodesWithANullOne()
    {
        var frame = ApproverProtocol.Encode(Request() with { ClientLabel = null });

        Assert.DoesNotContain("client_label", Encoding.UTF8.GetString(frame), StringComparison.Ordinal);
        Assert.True(ApproverProtocol.TryDecode(frame, out CredentialRequest? decoded));
        Assert.Null(decoded.ClientLabel);
    }

    [Fact]
    public void ACredentialReplySurvivesTheRoundTrip()
    {
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Granted()), out CredentialReply? decoded));

        // This one really can compare whole records: nothing on it is a collection.
        Assert.Equal(Granted(), decoded);
        Assert.Equal("sk_live_sentinel", decoded.Value, StringComparer.Ordinal);
    }

    [Fact]
    public void ANamesReplySurvivesTheRoundTrip()
    {
        var reply = new NamesReply(true, [new EntryName("env/dev", "STRIPE_KEY"), new EntryName("", "LOOSE")], "", true);

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out NamesReply? decoded));

        Assert.True(decoded.VaultUnlocked);
        Assert.Equal(reply.Names, decoded.Names);
        Assert.Equal(reply.Reason, decoded.Reason, StringComparer.Ordinal);
    }

    [Fact]
    public void ANamesRequestSurvivesTheRoundTrip()
    {
        var request = new NamesRequest(["env/**", "servers/staging/*"]);

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(request), out NamesRequest? decoded));

        Assert.Equal(request.Exposure, decoded.Exposure);
    }

    /// <summary>An ordinary name, encoding to seventy-six bytes with its separating comma.</summary>
    private static EntryName Ordinary(int i) =>
        new("env/dev/services", $"SERVICE_ACCOUNT_ACCESS_TOKEN_AB_{i:D4}");

    /// <summary>A title made of astral runes, each costing twelve encoded bytes.</summary>
    /// <remarks>
    /// The Unicode tag block. <see cref="System.Text.Json.Utf8JsonWriter"/>'s default encoder emits
    /// <c>\uXXXX</c> per UTF-16 code unit, and an astral rune is two of them — so what is four bytes
    /// in UTF-8 is twelve on the wire, and the budget is spent at a few dozen names rather than a
    /// thousand. Escaping, not length, is what makes a count-based cap unable to do this job.
    /// </remarks>
    private static EntryName Astral(int runes) =>
        new("env/dev", string.Concat(Enumerable.Repeat("\U000E0041", runes)));

    /// <summary>
    /// The reproduced case: a thousand ordinary names, which cost 76,060 bytes to encode whole.
    /// </summary>
    /// <remarks>
    /// The sixty-one byte envelope plus a thousand seventy-five byte elements and their nine hundred
    /// and ninety-nine commas. That is over <see cref="MessageFramer.MaximumPayloadBytes"/>, so
    /// before this bound existed the write threw, <see cref="ApproverListener"/> swallowed it, and
    /// the connection went down carrying its grants with it.
    /// </remarks>

    [Fact]
    public void AThousandNames_EncodeToAFrameTheFramerWillSend()
    {
        var reply = new NamesReply(true, [.. Enumerable.Range(0, 1000).Select(Ordinary)], string.Empty, true);

        Assert.True(ApproverProtocol.Encode(reply).Length <= MessageFramer.MaximumPayloadBytes);
    }

    /// <summary>A listing that did not all fit says so, and still carries what did.</summary>
    [Fact]
    public void AnOversizeListing_SaysItIsNotComplete()
    {
        var reply = new NamesReply(true, [.. Enumerable.Range(0, 1000).Select(Ordinary)], string.Empty, true);

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out NamesReply? decoded));

        Assert.False(decoded.Complete);

        // Both bounds, so neither "return nothing" nor "return everything" could pass this.
        Assert.NotEmpty(decoded.Names);
        Assert.True(decoded.Names.Count < 1000);
    }

    /// <summary>And one that did fit says that instead. Without this, "always incomplete" would pass.</summary>
    [Fact]
    public void AListingThatFits_SaysItIsComplete()
    {
        var reply = new NamesReply(true, [Ordinary(1), Ordinary(2)], string.Empty, true);

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out NamesReply? decoded));

        Assert.True(decoded.Complete);
        Assert.Equal(reply.Names, decoded.Names);
    }

    /// <summary>
    /// A names reply from before this field existed decodes as incomplete, not as complete.
    /// </summary>
    /// <remarks>
    /// The mixed-version case, and the reason the field is named for completeness rather than for
    /// truncation. That older approver capped its listing at a thousand entries and said nothing
    /// about it; reading its silence as "you have the lot" would turn it into a claim it never made.
    /// </remarks>
    [Fact]
    public void AReplyWithNoCompleteField_DecodesAsIncomplete()
    {
        var older = """{"v":1,"kind":"names","unlocked":true,"reason":"","names":[]}"""u8;

        Assert.True(ApproverProtocol.TryDecode(older, out NamesReply? decoded));

        Assert.False(decoded.Complete);
    }

    /// <summary>This version always says, so a bridge never has to infer it.</summary>
    [Fact]
    public void EveryNamesReplyThisVersionWrites_CarriesComplete()
    {
        foreach (var reply in new[]
                 {
                     new NamesReply(true, [Ordinary(1)], string.Empty, true),
                     new NamesReply(true, [.. Enumerable.Range(0, 1000).Select(Ordinary)], string.Empty, true),
                     new NamesReply(false, [], "no vault is unlocked", true),
                 })
        {
            using var document = JsonDocument.Parse(ApproverProtocol.Encode(reply));

            Assert.True(document.RootElement.TryGetProperty("complete", out var complete));
            Assert.True(complete.ValueKind is JsonValueKind.True or JsonValueKind.False);
        }
    }

    /// <summary>
    /// Adding it did not bump the wire version, and must not.
    /// </summary>
    /// <remarks>
    /// The same argument <c>client_label</c> settled in 2.3: a bump makes every mixed-version pair
    /// fail at the framing layer, with no reply and no audit line beyond <c>no-approver</c>, over
    /// one optional field. Both sides degrade to "assume incomplete" instead, which is true.
    /// </remarks>
    [Fact]
    public void AddingCompleteDidNotBumpTheWireVersion()
    {
        Assert.Equal(1, ApproverProtocol.Version);

        using var document = JsonDocument.Parse(
            ApproverProtocol.Encode(new NamesReply(true, [Ordinary(1)], string.Empty, true)));

        Assert.Equal(1, document.RootElement.GetProperty("v").GetInt32());
    }

    /// <summary>
    /// The lister's ceiling sits above the most names any frame could carry, so it can never be the
    /// reason a name is missing from a reply that calls itself complete.
    /// </summary>
    /// <remarks>
    /// Two bounds that must not overlap: the lister caps how much work the approver does, and the
    /// encoder caps what an agent sees. Were the lister's the tighter of the two, it would drop
    /// names the encoder never saw — and the encoder would then honestly report a complete listing
    /// that was not one. That is the original defect, moved one layer up, so it is asserted with the
    /// smallest element the format admits rather than argued about.
    /// </remarks>
    [Fact]
    public void NoFrameCanHoldMoreNamesThanTheListersCap()
    {
        var smallest = new NamesReply(
            true,
            [.. Enumerable.Repeat(new EntryName(string.Empty, string.Empty), VaultEntryNameLister.MaximumNames)],
            string.Empty,
            true);

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(smallest), out NamesReply? decoded));

        Assert.False(decoded.Complete);
        Assert.True(decoded.Names.Count < VaultEntryNameLister.MaximumNames);
    }

    /// <summary>
    /// A name that escapes to six bytes per code unit spends the budget long before any entry count
    /// does, and what survives is still exactly what the vault holds.
    /// </summary>
    [Theory]
    [InlineData("\U000E0041")]
    [InlineData("\U0001F468\u200D\U0001F4BB")]
    [InlineData("\"\\<>&+")]
    [InlineData("\u2028\u2029")]
    [InlineData("\u202E")]
    public void NamesThatEscapeToSixBytesEach_StillFitOneFrame(string hostile)
    {
        var name = new EntryName("env/dev", string.Concat(Enumerable.Repeat(hostile, 128)));
        var frame = ApproverProtocol.Encode(new NamesReply(true, [.. Enumerable.Repeat(name, 500)], string.Empty, true));

        Assert.True(frame.Length <= MessageFramer.MaximumPayloadBytes);
        Assert.True(ApproverProtocol.TryDecode(frame, out NamesReply? decoded));

        // Non-vacuous in both directions: something was dropped, and what survived is unaltered.
        Assert.False(decoded.Complete);
        Assert.NotEmpty(decoded.Names);
        Assert.True(decoded.Names.Count < 500);
        Assert.All(decoded.Names, kept => Assert.Equal(name, kept));
    }

    /// <summary>
    /// One name no frame could hold yields a valid, empty frame rather than an unsendable one.
    /// </summary>
    /// <remarks>
    /// A name is dropped whole or not at all. Shortening it would put the approver in the business
    /// of altering names — which <see cref="Approval.IEntryNameLister"/> deliberately leaves to
    /// whoever renders them — and would hand the bridge a string that is not the vault's.
    /// </remarks>
    [Fact]
    public void OneNameTooBigForAnyFrame_YieldsAnEmptyReply()
    {
        var frame = ApproverProtocol.Encode(new NamesReply(true, [Astral(100_000)], "the vault is open", true));

        Assert.True(frame.Length <= MessageFramer.MaximumPayloadBytes);
        Assert.True(ApproverProtocol.TryDecode(frame, out NamesReply? decoded));

        Assert.Empty(decoded.Names);
        Assert.False(decoded.Complete);
        Assert.True(decoded.VaultUnlocked);
    }

    /// <summary>What arrives is a prefix of what was asked for: dropped from the end, never the middle.</summary>
    [Fact]
    public void TheNamesThatFit_AreAPrefixOfWhatWasAsked()
    {
        var asked = Enumerable.Range(0, 1000).Select(Ordinary).ToArray();

        Assert.True(ApproverProtocol.TryDecode(
            ApproverProtocol.Encode(new NamesReply(true, asked, string.Empty, true)), out NamesReply? decoded));

        Assert.Equal(asked.Take(decoded.Names.Count), decoded.Names);
    }

    /// <summary>
    /// The most dangerous line in this file's subject matter, and it is a <c>ToString</c>. A record
    /// prints every member by default, so one interpolated string in a log line, an exception
    /// message or a trace would put a live credential somewhere it can never be taken back from.
    /// </summary>
    [Fact]
    public void AReplyNeverPrintsTheCredentialItCarries()
    {
        var reply = Granted("sk_live_leak_me");

        var printed = $"{reply}";

        Assert.DoesNotContain("sk_live_leak_me", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_live_leak_me", reply.ToString(), StringComparison.Ordinal);
        Assert.Contains("redacted", printed, StringComparison.Ordinal);

        // ...and the value is still genuinely there, so this is not passing because the field is
        // empty. That is the trap a "no secret in the output" test usually falls into.
        Assert.Equal("sk_live_leak_me", reply.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// A grant with nothing in it is not a grant. Decoding it as one would hand an agent an empty
    /// string dressed as a credential, which is the failure mode that looks like success.
    /// </summary>
    [Fact]
    public void AGrantedReplyWithNoValue_IsRefused()
    {
        var frame = Encoding.UTF8.GetBytes(
            """{"v":1,"kind":"credential","decision":"granted","method":5,"reason":"ok","ttl_seconds":300}""");

        Assert.False(ApproverProtocol.TryDecode(frame, out CredentialReply? reply));
        Assert.Null(reply);
    }

    /// <summary>
    /// The delimiter cannot be forged. Frames are newline-separated, so a reason containing a
    /// newline would otherwise let one message be read as two.
    /// </summary>
    [Fact]
    public void NoEncodedFrame_ContainsTheDelimiter()
    {
        var hostile = "line one\nline two\r\n{\"kind\":\"credential\",\"decision\":\"granted\"}";

        foreach (var frame in new[]
                 {
                     ApproverProtocol.Encode(Request(hostile)),
                     ApproverProtocol.Encode(Granted() with { Reason = hostile }),
                     ApproverProtocol.Encode(Granted(hostile + new string('n', 100_000))),
                     ApproverProtocol.Encode(new NamesReply(true, [new EntryName(hostile, hostile)], hostile, true)),
                 })
        {
            Assert.DoesNotContain((byte)'\n', frame);
            Assert.DoesNotContain((byte)'\r', frame);
        }
    }

    [Fact]
    public void AReasonWithANewlineStillArrivesIntact()
    {
        var hostile = "first\nsecond";

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Request(hostile)), out CredentialRequest? decoded));

        Assert.Equal(hostile, decoded.Reason, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("""{"v":1}""")]
    [InlineData("""{"v":1,"kind":"credential"}""")]
    [InlineData("""{"v":2,"kind":"credential","entry":"a","field":"password","reason":"r","ttl_seconds":1,"exposure":[]}""")]
    [InlineData("""{"v":1,"kind":"nonsense","entry":"a","field":"password","reason":"r","ttl_seconds":1,"exposure":[]}""")]
    [InlineData("""{"v":1,"kind":"credential","entry":1,"field":"password","reason":"r","ttl_seconds":1,"exposure":[]}""")]
    [InlineData("""{"v":1,"kind":"credential","entry":"a","field":"password","reason":"r","ttl_seconds":"nine","exposure":[]}""")]
    [InlineData("""{"v":1,"kind":"credential","entry":"a","field":"password","reason":"r","ttl_seconds":1,"exposure":[7]}""")]
    public void AMalformedFrame_IsRefusedRatherThanThrowing(string json)
    {
        var frame = Encoding.UTF8.GetBytes(json);

        Assert.False(ApproverProtocol.TryDecode(frame, out CredentialRequest? request));
        Assert.Null(request);
    }

    /// <summary>
    /// A version this build does not speak is refused rather than best-guessed. The bridge turns
    /// that into a denial, which is the fail-closed direction when two halves of keypaste have
    /// drifted apart.
    /// </summary>
    [Fact]
    public void AFrameFromAnotherVersion_IsRefused()
    {
        var frame = Encoding.UTF8.GetBytes(
            """{"v":99,"kind":"names","unlocked":true,"reason":"","names":[]}""");

        Assert.False(ApproverProtocol.TryDecode(frame, out NamesReply? reply));
        Assert.Null(reply);
    }

    /// <summary>
    /// A method number from a newer approver becomes <see cref="AuditMethod.Failed"/> rather than
    /// refusing the whole reply, so the exchange still produces a denial <em>and</em> an audit line.
    /// Refusing outright would lose the line, which is the one thing law 3.3 will not have.
    /// </summary>
    [Fact]
    public void AnUnknownMethod_BecomesAFailureRatherThanLosingTheReply()
    {
        var frame = Encoding.UTF8.GetBytes(
            """{"v":1,"kind":"credential","decision":"denied","method":9999,"reason":"who knows","ttl_seconds":0}""");

        Assert.True(ApproverProtocol.TryDecode(frame, out CredentialReply? reply));
        Assert.Equal(AuditMethod.Failed, reply.Method);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
    }

    /// <summary>
    /// A denied reply that somehow carries a value must not smuggle it through. Belt and braces:
    /// nothing keypaste writes does this, and a peer is not keypaste.
    /// </summary>
    [Fact]
    public void ADeniedReplyCarryingAValue_ArrivesWithoutIt()
    {
        var frame = Encoding.UTF8.GetBytes(
            """{"v":1,"kind":"credential","decision":"denied","method":7,"reason":"nobody answered","ttl_seconds":0,"value":"sk_live_smuggled"}""");

        Assert.True(ApproverProtocol.TryDecode(frame, out CredentialReply? reply));
        Assert.Null(reply.Value);
    }

    [Fact]
    public void KindOfNamesTheMessageWithoutCommittingToParsingIt()
    {
        Assert.Equal(ApproverMessageKind.Credential, ApproverProtocol.KindOf(ApproverProtocol.Encode(Request())));
        Assert.Equal(ApproverMessageKind.Names, ApproverProtocol.KindOf(ApproverProtocol.Encode(new NamesRequest([]))));
        Assert.Equal(ApproverMessageKind.Unknown, ApproverProtocol.KindOf(Encoding.UTF8.GetBytes("{}")));
        Assert.Equal(ApproverMessageKind.Unknown, ApproverProtocol.KindOf(Encoding.UTF8.GetBytes("nope")));
    }

    [Fact]
    public void EveryFrameCarriesItsVersion()
    {
        using var parsed = JsonDocument.Parse(ApproverProtocol.Encode(Request()));

        Assert.Equal(ApproverProtocol.Version, parsed.RootElement.GetProperty("v").GetInt32());
    }

    /// <summary>A note nobody could send is answered with a refusal, not with an unsendable frame.</summary>
    /// <remarks>
    /// <c>notes</c> is a releasable field and a KDBX note has no length limit, so this is an
    /// ordinary vault entry rather than a hostile one. Before F.3d the frame was written anyway,
    /// <see cref="MessageFramer"/> threw on the write, and the connection went down with the grants
    /// scoped to it — after a person had already approved the release.
    /// </remarks>
    [Fact]
    public void AGrantedReplyTooBigForOneFrame_IsEncodedAsARefusal()
    {
        var frame = ApproverProtocol.Encode(Granted(new string('n', 100_000)));

        Assert.True(frame.Length <= MessageFramer.MaximumPayloadBytes);
        Assert.True(ApproverProtocol.TryDecode(frame, out CredentialReply? reply));

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Undeliverable, reply.Method);
        Assert.Null(reply.Value);
        Assert.Equal(0, reply.TtlSeconds);

        // The entry survives, because the audit line the bridge writes out of this reply exists to
        // answer which entry it was.
        Assert.Equal("env/dev/STRIPE_KEY", reply.Entry, StringComparer.Ordinal);
    }

    /// <summary>
    /// The refusal carries no part of the value. This is the test that catches "truncate it instead".
    /// </summary>
    /// <remarks>
    /// A shortened credential is not a shorter credential, it is a different one — and a prefix of a
    /// live secret on the wire is most of what a bounded reply exists to prevent. The sentinel sits
    /// at the front of the value, which is where a truncation would keep it.
    /// </remarks>
    [Fact]
    public void AnUndeliverableReply_CarriesNoByteOfTheValue()
    {
        const string sentinel = "sk_live_SENTINEL_9a3e21";

        var frame = ApproverProtocol.Encode(Granted(sentinel + new string('n', 100_000)));

        Assert.DoesNotContain(sentinel, Encoding.UTF8.GetString(frame), StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal says which authority the release had, and never invents one it did not have.
    /// </summary>
    /// <remarks>
    /// THREATS.md T-16: a release a standing rule authorized must never be written up as a human
    /// act, and this is the one path where a denial follows an authorization — so the sentence has
    /// to be chosen by the method the reply arrived with rather than written once for all three.
    /// </remarks>
    [Theory]
    [InlineData(AuditMethod.Prompt)]
    [InlineData(AuditMethod.GrantCache)]
    [InlineData(AuditMethod.Policy)]
    public void AnUndeliverableReply_NamesTheAuthorityItHad(AuditMethod authority)
    {
        var frame = ApproverProtocol.Encode(
            Granted(new string('n', 100_000)) with { Method = authority });

        Assert.True(ApproverProtocol.TryDecode(frame, out CredentialReply? reply));
        Assert.Equal(AuditMethod.Undeliverable, reply.Method);
        Assert.Equal(ApproverProtocol.UndeliverableReason(authority), reply.Reason, StringComparer.Ordinal);

        Assert.Equal(
            authority != AuditMethod.Policy,
            reply.Reason.Contains("person", StringComparison.Ordinal));
    }

    /// <summary>
    /// A refusal too long to send stays a refusal under its own method, rather than being reported
    /// as a release that could not be delivered.
    /// </summary>
    /// <remarks>
    /// The reachable case is <c>ApproverHandler</c>'s glob failure, which interpolates an
    /// operator-supplied parse error into its reason with no cap of its own. Nothing was authorized
    /// there and nothing was read, so borrowing the undeliverable-release sentence would put one
    /// untrue record in place of another.
    /// </remarks>
    [Fact]
    public void AnOverlongRefusal_StaysARefusalAndKeepsItsMethod()
    {
        var frame = ApproverProtocol.Encode(new CredentialReply
        {
            Decision = AuditDecision.Denied,
            Method = AuditMethod.Failed,
            Reason = new string('e', 100_000),
            Entry = "env/dev/STRIPE_KEY",
        });

        Assert.True(frame.Length <= MessageFramer.MaximumPayloadBytes);
        Assert.True(ApproverProtocol.TryDecode(frame, out CredentialReply? reply));

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Failed, reply.Method);
        Assert.Equal(ApproverProtocol.Oversized, reply.Reason, StringComparer.Ordinal);
    }

    /// <summary>
    /// The budget is spent in encoded bytes, not in characters somebody counted beforehand.
    /// </summary>
    /// <remarks>
    /// The default encoder escapes every non-ASCII UTF-16 code unit to <c>\uXXXX</c>, so one astral
    /// rune costs twelve bytes on the wire and two characters in memory. The two values here are the
    /// same number of <see cref="string"/> characters and differ by a factor of six once encoded,
    /// which is why any count taken outside this writer is a second answer waiting to disagree.
    /// </remarks>
    [Fact]
    public void AValueOfAstralRunes_IsMeasuredAsEncodedNotAsCharacters()
    {
        const int characters = 12_000;

        var astral = string.Concat(Enumerable.Repeat("\U0001F600", characters / 2));
        var ascii = new string('a', characters);

        Assert.Equal(astral.Length, ascii.Length);

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Granted(astral)), out CredentialReply? refused));
        Assert.Equal(AuditMethod.Undeliverable, refused.Method);

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Granted(ascii)), out CredentialReply? delivered));
        Assert.Equal(ascii, delivered.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// A reply that exactly fills a frame is still delivered, and one byte more is not.
    /// </summary>
    /// <remarks>
    /// Both halves in one test on purpose: the first fails if the bound is applied a byte early or
    /// the refusal becomes unconditional, and the second fails if it is applied a byte late. A
    /// credential is the one payload where "nearly" is not something this protocol can say.
    /// </remarks>
    [Fact]
    public void AReplyThatExactlyFillsAFrame_IsDeliveredAndOneByteMoreIsNot()
    {
        var envelope = ApproverProtocol.Encode(Granted(string.Empty)).Length;
        var exact = new string('a', MessageFramer.MaximumPayloadBytes - envelope);

        var fits = ApproverProtocol.Encode(Granted(exact));

        Assert.Equal(MessageFramer.MaximumPayloadBytes, fits.Length);
        Assert.True(ApproverProtocol.TryDecode(fits, out CredentialReply? delivered));
        Assert.Equal(exact, delivered.Value, StringComparer.Ordinal);

        Assert.True(
            ApproverProtocol.TryDecode(ApproverProtocol.Encode(Granted(exact + "a")), out CredentialReply? refused));
        Assert.Equal(AuditMethod.Undeliverable, refused.Method);
        Assert.Null(refused.Value);
    }

    /// <summary>
    /// No credential reply, however it is built, encodes to a frame this transport cannot send.
    /// </summary>
    /// <remarks>
    /// The property rather than the cases: every member that can grow is grown here, including two
    /// that grow together, and each has to come back as a frame inside the budget that still
    /// decodes. <c>MessageFramerTests.WhatTheProtocolEncodes_IsAlwaysWritable</c> ties the same
    /// claim to the guard that would otherwise end the connection.
    /// </remarks>
    [Fact]
    public void EveryCredentialReplyThisVersionEncodes_FitsOneFrame()
    {
        var huge = new string('n', 100_000);
        var hostile = string.Concat(Enumerable.Repeat("\"<&> \U0001F600", 20_000));

        foreach (var reply in new[]
                 {
                     Granted(huge),
                     Granted(hostile),
                     Granted(huge) with { Entry = huge },
                     Granted(huge) with { Reason = huge },
                     Granted(string.Empty) with { Entry = huge, Reason = huge },
                     new CredentialReply
                     {
                         Decision = AuditDecision.Denied,
                         Method = AuditMethod.OutOfScope,
                         Reason = hostile,
                         Entry = huge,
                     },
                 })
        {
            var frame = ApproverProtocol.Encode(reply);

            Assert.True(
                frame.Length <= MessageFramer.MaximumPayloadBytes,
                $"encoded {frame.Length} bytes, over the {MessageFramer.MaximumPayloadBytes}-byte budget");

            Assert.True(ApproverProtocol.TryDecode(frame, out CredentialReply? decoded));
            Assert.NotNull(decoded);
        }
    }

    [Fact]
    public void EncodingRejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => ApproverProtocol.Encode((CredentialRequest)null!));
        Assert.Throws<ArgumentNullException>(() => ApproverProtocol.Encode((CredentialReply)null!));
        Assert.Throws<ArgumentNullException>(() => ApproverProtocol.Encode((NamesRequest)null!));
        Assert.Throws<ArgumentNullException>(() => ApproverProtocol.Encode((NamesReply)null!));
    }
}
