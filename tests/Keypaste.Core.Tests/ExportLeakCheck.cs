using Keypaste.Core.Import;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Everything an import says about an export, in its plan and of each item it read, checked for the export's values.</summary>
internal static class ExportLeakCheck
{
    /// <summary>Asserts that no text the plan or the source shows a person holds any of <paramref name="values"/>.</summary>
    /// <param name="source">The export as read.</param>
    /// <param name="plan">The plan made from it.</param>
    /// <param name="values">The secrets and notes the export holds.</param>
    public static void AssertNothingSaidHoldsAValue(ExportSource source, ExportPlan plan, IEnumerable<string> values)
    {
        List<string?> said =
        [
            plan.Into,
            plan.Refusal,
            .. plan.Rows.SelectMany(row => new[] { row.Item, row.Note, row.ToString() }),
            .. plan.Losses.SelectMany(loss => new[] { loss.Item, loss.Part, loss.Reason, loss.ToString() }),
            .. source.Unread.SelectMany(unread => new[] { unread.Part, unread.Reason, unread.ToString() }),
            .. source.Items.Select(item => item.ToString()),
            .. source.Items.SelectMany(item => item.Omitted).Select(omitted => omitted.ToString()),
            .. source.Items.SelectMany(item => item.Fields).Select(field => field.ToString()),
            .. source.Items.SelectMany(item => item.History).Select(version => version.ToString()),
            .. source.Items.SelectMany(item => item.Passkeys).Select(passkey => passkey.ToString()),
            .. source.Items.SelectMany(item => item.Attachments).Select(attachment => attachment.ToString()),
        ];

        Assert.All(values, value => Assert.DoesNotContain(said, text => text is not null && text.Contains(value, StringComparison.Ordinal)));
    }
}
