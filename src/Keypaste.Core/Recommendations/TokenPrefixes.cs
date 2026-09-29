namespace Keypaste.Core.Recommendations;

/// <summary>A kind of well-known token, and the field a moved one becomes.</summary>
/// <param name="Name">What the person is shown, such as "GitHub token".</param>
/// <param name="Field">The env-named field a moved token is written to.</param>
public sealed record TokenKind(string Name, string Field);

/// <summary>The short list of token prefixes the notes check recognises (C.2).</summary>
/// <remarks>
/// Each prefix is one its service documents as its own, so a line that is exactly such a token is a
/// secret rather than a guess. A token counts only as a whole line: the prefix, then at least
/// sixteen characters of <c>[A-Za-z0-9_-]</c>, or for an AWS access key ID exactly sixteen of
/// <c>[A-Z0-9]</c>. The longest matching prefix names the kind.
/// </remarks>
public static class TokenPrefixes
{
    private const int _minimumBody = 16;
    private const int _awsBody = 16;

    private static readonly TokenKind _gitHub = new("GitHub token", "GITHUB_TOKEN");
    private static readonly TokenKind _gitLab = new("GitLab token", "GITLAB_TOKEN");
    private static readonly TokenKind _stripe = new("Stripe key", "STRIPE_SECRET_KEY");
    private static readonly TokenKind _anthropic = new("Anthropic key", "ANTHROPIC_API_KEY");
    private static readonly TokenKind _openAi = new("OpenAI key", "OPENAI_API_KEY");
    private static readonly TokenKind _aws = new("AWS access key ID", "AWS_ACCESS_KEY_ID");
    private static readonly TokenKind _slack = new("Slack token", "SLACK_TOKEN");
    private static readonly TokenKind _npm = new("npm token", "NPM_TOKEN");

    private static readonly (string Prefix, TokenKind Kind)[] _prefixes =
    [
        .. new (string Prefix, TokenKind Kind)[]
        {
            ("github_pat_", _gitHub), ("ghp_", _gitHub), ("gho_", _gitHub), ("ghu_", _gitHub), ("ghs_", _gitHub), ("ghr_", _gitHub),
            ("glpat-", _gitLab),
            ("sk_live_", _stripe), ("sk_test_", _stripe), ("rk_live_", _stripe), ("rk_test_", _stripe),
            ("sk-ant-", _anthropic),
            ("sk-proj-", _openAi), ("sk-svcacct-", _openAi), ("sk-", _openAi),
            ("AKIA", _aws), ("ASIA", _aws),
            ("xoxb-", _slack), ("xoxp-", _slack), ("xoxa-", _slack), ("xoxr-", _slack), ("xoxs-", _slack),
            ("npm_", _npm),
        }.OrderByDescending(pair => pair.Prefix.Length),
    ];

    /// <summary>Every recognised prefix and the kind it names, longest first.</summary>
    public static IReadOnlyList<(string Prefix, TokenKind Kind)> Known => _prefixes;

    /// <summary>The kind of token a whole trimmed line is, or null.</summary>
    /// <param name="line">One line of notes, already trimmed.</param>
    /// <returns>The kind, or null when the line is not exactly one recognised token.</returns>
    public static TokenKind? Match(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        foreach (var (prefix, kind) in _prefixes)
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var body = line.AsSpan(prefix.Length);
            var whole = ReferenceEquals(kind, _aws) ? IsAwsBody(body) : IsBody(body);
            return whole ? kind : null;
        }

        return null;
    }

    private static bool IsBody(ReadOnlySpan<char> body)
    {
        if (body.Length < _minimumBody)
        {
            return false;
        }

        foreach (var c in body)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAwsBody(ReadOnlySpan<char> body)
    {
        if (body.Length != _awsBody)
        {
            return false;
        }

        foreach (var c in body)
        {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
