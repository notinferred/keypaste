using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Policy;

/// <summary>The rules in force, in the order the file wrote them.</summary>
/// <remarks>
/// <para>
/// <b>All of the file, or none of it.</b> A section keypaste cannot use invalidates the whole
/// document rather than being skipped. Skipping would leave a <em>different</em> policy in force
/// than the one the human wrote, and on this path there is no way to know whether the difference is
/// narrower or wider — so the only safe reading of a file that is partly wrong is that it says
/// nothing (CORE.md law 3.7). Everything then prompts, which is exactly the state Stage 2.2 shipped.
/// </para>
/// <para>
/// This is the deliberate opposite of how <c>--expose</c> treats a bad glob, where the front end
/// refuses to start. The difference is which way the failure points: a malformed exposure would
/// leave a server running with no scope at all, while a malformed policy leaves a human being asked
/// about everything. Refusing to start on a bad policy file would turn a typo — or a planted
/// <c>chmod 000</c> — into a denial of service on the approver.
/// </para>
/// <para>
/// <b>First match wins, and a rule that matches decides.</b> Evaluation does not fall through to a
/// later rule when the first one has spent its hourly allowance; otherwise the cap is defeated by
/// writing the same rule twice. <c>keypaste policy ls</c> numbers the rules and says so.
/// </para>
/// </remarks>
public sealed class PolicyDocument
{
    private readonly PolicyRule[] _rules;

    private PolicyDocument(PolicyRule[] rules) => _rules = rules;

    /// <summary>No rules at all, which is what a missing, empty or unusable file means.</summary>
    public static PolicyDocument None { get; } = new([]);

    /// <summary>The rules, in file order.</summary>
    public IReadOnlyList<PolicyRule> Rules => _rules;

    /// <summary>Builds a document from parsed syntax, refusing the whole file on any problem.</summary>
    /// <param name="syntax">What the reader made of the file.</param>
    /// <param name="document">The rules, or <see cref="None"/> on failure.</param>
    /// <param name="error">A message in the shape <c>line 7: ...</c>, or empty on success.</param>
    /// <returns><see langword="true"/> if every section was a usable rule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="syntax"/> is null.</exception>
    public static bool TryCreate(
        TomlDocument syntax,
        [NotNullWhen(true)] out PolicyDocument? document,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(syntax);

        document = null;

        var rules = new PolicyRule[syntax.Tables.Count];

        for (var i = 0; i < syntax.Tables.Count; i++)
        {
            if (!PolicyRule.TryCreate(syntax.Tables[i], i + 1, out var rule, out error))
            {
                return false;
            }

            rules[i] = rule;
        }

        document = rules.Length == 0 ? None : new PolicyDocument(rules);
        error = string.Empty;
        return true;
    }

    /// <summary>Finds the first rule covering a request.</summary>
    /// <param name="clientLabel">The operator's label for the asking bridge, or null if it set none.</param>
    /// <param name="name">The resolved, unsanitized entry name.</param>
    /// <param name="field">The field asked for.</param>
    /// <param name="rule">The rule, when one covers it.</param>
    /// <returns><see langword="true"/> if a rule covers the request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="field"/> is null.</exception>
    public bool TryMatch(
        string? clientLabel,
        EntryName name,
        string field,
        [NotNullWhen(true)] out PolicyRule? rule)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);

        foreach (var candidate in _rules)
        {
            if (candidate.Matches(clientLabel, name, field))
            {
                rule = candidate;
                return true;
            }
        }

        rule = null;
        return false;
    }
}
