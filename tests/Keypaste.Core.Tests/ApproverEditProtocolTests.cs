using System.Text;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The <c>edit</c> frame (D-0464): every action round-trips, a recipe goes as the recipe so no generated
/// value crosses, a value and a recipe together or neither is refused, and nothing prints a value.
/// </summary>
public sealed class ApproverEditProtocolTests
{
    private const string _sentinel = "EDIT-WIRE-SENTINEL-9b30";

    private static readonly EntryName _name = new("work", "github");

    public static TheoryData<TerminalEdit> Edits => new(
        new AddItem(_name, "octo", "https://github.example", "notes", NewSecret.Of(_sentinel)),
        new SetPassword(_name, NewSecret.Of(_sentinel)),
        new SetPassword(_name, NewSecret.Generated(SecretRecipe.For(new PassphraseRecipe { WordCount = 6, Separator = '-' }))),
        new SetFields(_name, [new FieldWrite("KEY", _sentinel, Protect: false), new FieldWrite("OTHER", "o", Protect: false)]),
        new RemoveItem("work/github"),
        new RotatePassword(_name, SecretRecipe.Default),
        new RemoveField("work/github", "KEY"),
        new EnvSetKey("billing", "prod", "API_KEY", "work/github", NewSecret.Of(_sentinel)),
        new EnvRemoveKey("billing", "dev", "API_KEY", null),
        new EnvPullFile("billing", "dev", null, $"API_KEY={_sentinel}\n"),
        new TagItem("billing", "dev", "work/github", Tagging: true),
        new TagItem("billing", "dev", "work/github", Tagging: false));

    private static EditRequest Request(TerminalEdit edit) => new(edit) { Vault = "/vault.kdbx", Session = "session-one" };

    [Theory]
    [MemberData(nameof(Edits))]
    public void Every_action_round_trips(TerminalEdit edit)
    {
        var frame = ApproverProtocol.Encode(Request(edit));

        Assert.Equal(ApproverMessageKind.Edit, ApproverProtocol.KindOf(frame));
        Assert.True(ApproverProtocol.TryDecode(frame, out EditRequest? decoded));
        Assert.Equal(("/vault.kdbx", "session-one"), (decoded.Vault, decoded.Session));
        Assert.Equal(edit.Action, decoded.Edit.Action);
        Assert.Equal(Encoding.UTF8.GetString(frame), Encoding.UTF8.GetString(ApproverProtocol.Encode(decoded)));
        Assert.DoesNotContain(_sentinel, decoded.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_recipe_goes_as_the_recipe_and_never_as_a_value()
    {
        var frame = Encoding.UTF8.GetString(ApproverProtocol.Encode(Request(new SetPassword(_name, NewSecret.Generated(SecretRecipe.Default)))));

        Assert.Contains("\"generate\":", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\":", frame, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"v":2,"kind":"edit","vault":"/v","session":"s","action":"set","group":"work","title":"github"}""")]
    [InlineData("""{"v":2,"kind":"edit","vault":"/v","session":"s","action":"set","group":"work","title":"github","value":"x","generate":{"kind":"characters","length":20,"alphabet":15}}""")]
    [InlineData("""{"v":2,"kind":"edit","vault":"/v","session":"s","action":"set","group":"work","title":"github","value":null}""")]
    [InlineData("""{"v":2,"kind":"edit","vault":"/v","session":"s","action":"rotate","group":"work","title":"github","generate":{"kind":"characters","length":2,"alphabet":15}}""")]
    [InlineData("""{"v":2,"kind":"edit","vault":"/v","session":"s","action":"set-fields","group":"work","title":"github","fields":[]}""")]
    [InlineData("""{"v":2,"kind":"edit","vault":"/v","session":"s","action":"chmod","item":"x"}""")]
    [InlineData("""{"v":2,"kind":"edit","vault":"/v","session":"s","action":"rm","item":"x","item":"y"}""")]
    public void A_malformed_edit_is_refused(string frame)
    {
        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out EditRequest? _));
    }

    [Fact]
    public void A_reply_round_trips_with_the_variables_it_wrote()
    {
        var home = new EntryName("env/billing", ".env");
        var reply = new EditReply(AuditDecision.Granted, AuditMethod.Prompt, "a person allowed it")
        {
            Recorded = true,
            Done = new EditDone(EditAction.EnvPull, EditOutcome.Imported, "billing/dev")
            {
                Keys = [new EnvKeyWrite("A", EnvWriteChange.New, home, "A"), new EnvKeyWrite("B", EnvWriteChange.Unchanged, home, "B")],
                CreatesHome = true,
            },
        };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out EditReply? decoded));
        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt, true), (decoded.Decision, decoded.Method, decoded.Recorded));
        Assert.Equal(EditOutcome.Imported, decoded.Done!.Outcome);
        Assert.True(decoded.Done.CreatesHome);
        Assert.Equal([("A", EnvWriteChange.New), ("B", EnvWriteChange.Unchanged)], decoded.Done.Keys.Select(key => (key.Key, key.Change)));
    }

    [Fact]
    public void A_refusal_keeps_its_kind_and_an_unknown_method_is_a_failure()
    {
        var refused = new EditReply(AuditDecision.Denied, AuditMethod.InvalidRequest, "no item 'x'") { Refusal = EditRefusalKind.NotFound };
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(refused), out EditReply? decoded));
        Assert.Equal(EditRefusalKind.NotFound, decoded.Refusal);

        var unknown = """{"v":2,"kind":"edit","decision":"granted","method":999,"reason":"?"}""";
        Assert.True(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(unknown), out EditReply? future));
        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed), (future.Decision, future.Method));
    }

    [Fact]
    public void An_edit_too_large_for_one_frame_does_not_fit()
    {
        var large = new EnvPullFile("billing", "dev", null, new string('A', MessageFramer.MaximumPayloadBytes));

        Assert.False(ApproverProtocol.Fits(Request(large)));
        Assert.True(ApproverProtocol.Fits(Request(new RemoveItem("work/github"))));
    }
}
