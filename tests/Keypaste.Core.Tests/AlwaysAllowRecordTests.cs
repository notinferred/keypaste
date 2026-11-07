using Keypaste.Core.Agents;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The record's <c>always</c> member, the retired mode word and the mode a record leaving it out gets.</summary>
public sealed class AlwaysAllowRecordTests
{
    private const string _item = "0123456789abcdef0123456789abcdef";
    private const string _other = "fedcba9876543210fedcba9876543210";

    private static readonly AlwaysCommand _command = new(
        "/usr/bin/npm",
        ["npm", "run", "migrate"],
        "/work/billing",
        [new AlwaysVariable("STRIPE_KEY", _item, "password"), new AlwaysVariable("DATABASE_URL", _other, "DATABASE_URL")]);

    [Fact]
    public void Rows_round_trip_and_the_record_stays_wholly_understood()
    {
        var access = AgentAccess.Defaults with { Always = new AlwaysAllowed { Items = [_item], Commands = [_command] } };

        var read = AcafRecord.Read(AcafRecord.Write(access));

        Assert.True(read.Understood);
        Assert.Equal(access, read.Access);
    }

    [Fact]
    public void A_record_with_no_rows_writes_no_member_and_the_defaults_ask()
    {
        Assert.Equal("""{"acaf":1,"see":["tag:env:*"],"values":true,"run":false,"mode":"ask"}""", AcafRecord.Write(AgentAccess.Defaults));
        Assert.Equal(AgentMode.Ask, AcafRecord.Read("""{"acaf":1}""").Access.Mode);
    }

    [Fact]
    public void The_retired_word_reads_as_ask_and_a_change_never_writes_it_again()
    {
        const string stored = """{"acaf":1,"see":["tag:env:*"],"values":true,"run":false,"mode":"allow-for-a-while","projects":[{"project":"billing","mode":"allow-for-a-while"}]}""";

        var read = AcafRecord.Read(stored);

        Assert.True(read.Understood);
        Assert.Equal(AgentMode.Ask, read.Access.Mode);
        Assert.Equal(AgentMode.Ask, Assert.Single(read.Access.Projects).Mode);

        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(AccessEdit.AlwaysItem(_item), [stored], out var written));
        Assert.DoesNotContain("allow-for-a-while", written, StringComparison.Ordinal);
    }

    [Fact]
    public void The_retired_word_is_refused_as_a_new_setting()
    {
        Assert.False(AgentModes.TryParse("allow-for-a-while", out _));
        Assert.False(AccessEdit.TryCreate(null, "allow-for-a-while", null, null, null, null, null, false, out _, out var error));
        Assert.Equal("a mode is ask or automatic", error);
    }

    [Theory]
    [InlineData("""{"acaf":1,"always":[]}""")]
    [InlineData("""{"acaf":1,"always":{"items":"x"}}""")]
    [InlineData("""{"acaf":1,"always":{"items":["not hex"]}}""")]
    [InlineData("""{"acaf":1,"always":{"items":["0123456789abcdef0123456789abcdef","0123456789abcdef0123456789abcdef"]}}""")]
    [InlineData("""{"acaf":1,"always":{"other":[]}}""")]
    [InlineData("""{"acaf":1,"always":{"commands":[{"program":"/bin/x"}]}}""")]
    [InlineData("""{"acaf":1,"always":{"commands":[{"program":"/bin/x","command":["x"],"directory":"/d","variables":[{"name":"K","item":"nope","field":"password"}]}]}}""")]
    public void A_list_not_understood_gives_no_row_and_the_record_applies_only_beneath_the_defaults(string text)
    {
        var read = AcafRecord.Read(text);

        Assert.False(read.Understood);
        Assert.True(read.Access.Always.IsEmpty);

        var access = EffectiveAccess.Create("claude-code", new AgentRecords(SavedRead.Current, [read], []), AccessCaps.Empty);

        Assert.False(access.AlwaysAllows(_item));
    }

    [Fact]
    public void A_list_not_understood_is_kept_and_a_row_is_not_placed_in_it()
    {
        const string stored = """{"acaf":1,"always":{"other":[]}}""";

        Assert.Equal(AgentAccessWrite.AlwaysUnreadable, AcafRecord.WriteChange(AccessEdit.AlwaysItem(_item), [stored], out _));
    }

    [Fact]
    public void Adding_keeps_every_other_row_and_member_and_removing_the_last_removes_the_member()
    {
        const string stored = """{"acaf":1,"see":["tag:env:*"],"values":true,"run":true,"mode":"ask","later":1}""";

        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(AccessEdit.AlwaysItem(_item), [stored], out var one));
        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(AccessEdit.AlwaysCommand(_command), [one], out var two));
        Assert.Contains("\"later\":1", two, StringComparison.Ordinal);

        var read = AcafRecord.Read(two).Said;
        Assert.Equal([_item], read.Always.Items);
        Assert.Equal([_command], read.Always.Commands);

        Assert.True(AccessEdit.TryForget(AlwaysAllowed.ItemId(_item), out var forgetItem, out _));
        Assert.True(AccessEdit.TryForget(_command.Id, out var forgetCommand, out _));
        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(forgetItem, [two], out var three));
        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(forgetCommand, [three], out var four));
        Assert.DoesNotContain("always", four, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reset_of_the_whole_agent_removes_the_rows()
    {
        var access = AgentAccess.Defaults with { Always = new AlwaysAllowed { Items = [_item] } };
        Assert.True(AccessEdit.TryCreate(null, null, null, null, null, null, null, true, out var reset, out _));

        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(reset, [AcafRecord.Write(access)], out var written));

        Assert.True(AcafRecord.Read(written).Access.Always.IsEmpty);
    }

    [Theory]
    [InlineData("/usr/bin/npm2", "npm run migrate", "/work/billing", "STRIPE_KEY")]
    [InlineData("/usr/bin/npm", "npm run migrate --force", "/work/billing", "STRIPE_KEY")]
    [InlineData("/usr/bin/npm", "npm migrate run", "/work/billing", "STRIPE_KEY")]
    [InlineData("/usr/bin/npm", "npm run migrate", "/work/other", "STRIPE_KEY")]
    [InlineData("/usr/bin/npm", "npm run migrate", "/work/billing", "OTHER_KEY")]
    public void Anything_different_is_another_command(string program, string line, string directory, string name)
    {
        var variables = new[] { new AlwaysVariable(name, _item, "password"), new AlwaysVariable("DATABASE_URL", _other, "DATABASE_URL") };

        Assert.NotEqual(_command, new AlwaysCommand(program, line.Split(' '), directory, variables));
    }

    [Fact]
    public void A_variable_from_another_item_or_field_is_another_command_and_their_order_is_not()
    {
        var same = new AlwaysCommand(_command.Program, _command.Command, _command.Directory, [.. _command.Variables.Reverse()]);
        var otherItem = new AlwaysCommand(_command.Program, _command.Command, _command.Directory, [new AlwaysVariable("STRIPE_KEY", _other, "password"), new AlwaysVariable("DATABASE_URL", _other, "DATABASE_URL")]);
        var otherField = new AlwaysCommand(_command.Program, _command.Command, _command.Directory, [new AlwaysVariable("STRIPE_KEY", _item, "username"), new AlwaysVariable("DATABASE_URL", _other, "DATABASE_URL")]);
        var fewer = new AlwaysCommand(_command.Program, _command.Command, _command.Directory, [new AlwaysVariable("STRIPE_KEY", _item, "password")]);

        Assert.Equal(_command, same);
        Assert.Equal(_command.Id, same.Id);
        Assert.NotEqual(_command, otherItem);
        Assert.NotEqual(_command, otherField);
        Assert.NotEqual(_command, fewer);
    }

    [Fact]
    public void A_row_is_a_gain_and_removing_one_gives_nothing()
    {
        var current = EffectiveAccess.Create("claude-code", new AgentRecords(SavedRead.Current, [AcafRecord.Read(AcafRecord.Write(AgentAccess.Defaults))], []), AccessCaps.Empty);

        var add = AccessChange.Classify(current, AccessEdit.AlwaysItem(_item).Apply(AgentAccess.Defaults));

        Assert.Equal(AccessDimension.AlwaysItem, Assert.Single(add.Gains).Dimension);

        var withRow = AgentAccess.Defaults with { Always = new AlwaysAllowed { Items = [_item] } };
        var held = EffectiveAccess.Create("claude-code", new AgentRecords(SavedRead.Current, [AcafRecord.Read(AcafRecord.Write(withRow))], []), AccessCaps.Empty);

        Assert.True(AccessEdit.TryForget(AlwaysAllowed.ItemId(_item), out var forget, out _));
        Assert.True(AccessChange.Classify(held, forget.Apply(withRow)).IsNarrowing);
    }

    [Fact]
    public void Every_record_that_bears_on_the_agent_must_hold_a_row()
    {
        var holds = AcafRecord.Read(AcafRecord.Write(AgentAccess.Defaults with { Always = new AlwaysAllowed { Items = [_item] } }));
        var lacks = AcafRecord.Read(AcafRecord.Write(AgentAccess.Defaults));

        Assert.True(EffectiveAccess.Create("claude-code", new AgentRecords(SavedRead.Current, [holds], []), AccessCaps.Empty).AlwaysAllows(_item));
        Assert.False(EffectiveAccess.Create("claude-code", new AgentRecords(SavedRead.Current, [holds, lacks], []), AccessCaps.Empty).AlwaysAllows(_item));
        Assert.False(EffectiveAccess.Create("claude-code", new AgentRecords(SavedRead.ChangedOnDisk, [holds], []), AccessCaps.Empty).AlwaysAllows(_item));
        Assert.False(EffectiveAccess.Create(null, new AgentRecords(SavedRead.Current, [], [holds]), AccessCaps.Empty).AlwaysAllows(_item));
    }
}
