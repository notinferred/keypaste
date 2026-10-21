namespace Keypaste.Core.Totp;

/// <summary>One TOTP code and how long it stays current.</summary>
/// <param name="Value">The code: digits, or Steam's letters and digits.</param>
/// <param name="Remaining">How long until the next period starts and the code changes.</param>
public sealed record TotpCode(string Value, TimeSpan Remaining)
{
    /// <summary>Never the code, so a log line or a debugger does not show it.</summary>
    /// <returns><c>TOTP code</c>.</returns>
    public override string ToString() => "TOTP code";
}
