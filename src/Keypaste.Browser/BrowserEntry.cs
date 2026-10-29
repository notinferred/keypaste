namespace Keypaste.Browser;

/// <summary>The one way into keypaste's browser code from <c>keypaste</c>.</summary>
public static class BrowserEntry
{
    /// <summary>
    /// <c>keypaste --selftest</c>: the box's known answers, in this process. CI requires them of each NativeAOT <c>keypaste</c>
    /// it builds, the one binary libsodium is linked into (D-0613).
    /// </summary>
    /// <returns>0 when the box gave every answer, or 1 when it gave a wrong one or libsodium could not be reached.</returns>
    public static int SelfTest(TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        string? disagreement;

        try
        {
            disagreement = KnownAnswers.Disagreement();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or CryptographicException)
        {
            disagreement = exception.Message;
        }

        if (disagreement is not null)
        {
            stderr.WriteLine($"keypaste: selftest failed: {disagreement}");
            return 1;
        }

        stdout.WriteLine(
            "keypaste: selftest ok: libsodium's NaCl box gave its known answers, opened and sealed the box TweetNaCl.js sealed, "
            + "and refused changed boxes and a public key of small order");
        return 0;
    }
}
