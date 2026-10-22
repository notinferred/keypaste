using System.Buffers;
using System.Globalization;

namespace Keypaste.Core.Totp;

/// <summary>How an item's TOTP codes are computed: its seed, HMAC, length and period (RFC 6238).</summary>
/// <remarks>
/// <para>
/// <see cref="Vault.ReadTotp"/> reads one as KeePassXC 2.7 does: from its older <c>TOTP Settings</c> and
/// <c>TOTP Seed</c> pair, from its <c>otp</c> field, an <c>otpauth://totp/</c> URI or KeeOTP's <c>key=</c> form, or
/// else from KeePass 2's <c>TimeOtp-*</c> fields. It computes the code KeePassXC shows or refuses one it cannot
/// compute as written, naming a field and never a value (D-0477).
/// </para>
/// <para>
/// The seed is held decoded only here, and <see cref="Dispose"/> zeroes it. No member, refusal or
/// <see cref="ToString"/> shows it, and a <see cref="TotpCode"/> does not show its code.
/// </para>
/// </remarks>
public sealed class TotpSpec : IDisposable
{
    /// <summary>KeePassXC's field, holding an <c>otpauth://totp/</c> URI.</summary>
    public const string OtpField = "otp";

    /// <summary>The fewest digits a code may have, as RFC 4226 requires.</summary>
    public const int FewestDigits = 6;

    /// <summary>The most digits a code may have.</summary>
    public const int MostDigits = 8;

    /// <summary>How many characters a Steam code has.</summary>
    public const int SteamDigits = 5;

    /// <summary>The longest period, in seconds: one day.</summary>
    public const int LongestPeriod = 86_400;

    private const string _keePass2Prefix = "TimeOtp-";
    private const string _scheme = "otpauth://";
    private const string _decimal = "0123456789";
    private const string _steam = "23456789BCDFGHJKMNPQRTVWXY";
    private const int _defaultDigits = 6;
    private const int _defaultPeriod = 30;

    private const string _schemeName = "otpauth:";
    private const string _steamEncoder = "steam";
    private const string _steamShortName = "S";
    private const string _legacySeed = "TOTP Seed";
    private const string _legacySettings = "TOTP Settings";

    private const string _uriSecret = "secret";
    private const string _uriAlgorithm = "algorithm";
    private const string _uriDigits = "digits";
    private const string _uriPeriod = "period";
    private const string _uriEncoder = "encoder";

    private const string _secret = "TimeOtp-Secret";
    private const string _secretHex = "TimeOtp-Secret-Hex";
    private const string _secretBase32 = "TimeOtp-Secret-Base32";
    private const string _secretBase64 = "TimeOtp-Secret-Base64";
    private const string _algorithm = "TimeOtp-Algorithm";
    private const string _length = "TimeOtp-Length";
    private const string _period = "TimeOtp-Period";

    private const string _keeOtpKey = "key";
    private const string _keeOtpSize = "size";
    private const string _keeOtpStep = "step";
    private const string _keeOtpAlgorithm = "otpHashMode";
    private const string _keeOtpType = "type";

    private static readonly string[] _uriParameters = [_uriSecret, _uriAlgorithm, _uriDigits, _uriPeriod, _uriEncoder];
    private static readonly string[] _keeOtpParameters = [_keeOtpKey, _keeOtpSize, _keeOtpStep, _keeOtpAlgorithm, _keeOtpType];
    private static readonly string[] _keePass2Secrets = [_secret, _secretHex, _secretBase32, _secretBase64];

    private readonly byte[] _seed;
    private readonly int _periodSeconds;
    private bool _disposed;

    private TotpSpec(byte[] seed, TotpAlgorithm algorithm, int digits, int periodSeconds, bool isSteam)
    {
        _seed = seed;
        _periodSeconds = periodSeconds;
        Algorithm = algorithm;
        Digits = digits;
        IsSteam = isSteam;
    }

    /// <summary>The HMAC codes are computed with.</summary>
    public TotpAlgorithm Algorithm { get; }

    /// <summary>How many characters a code has: <see cref="FewestDigits"/> to <see cref="MostDigits"/> digits, or <see cref="SteamDigits"/> for Steam.</summary>
    public int Digits { get; }

    /// <summary>How long each code is current.</summary>
    public TimeSpan Period => TimeSpan.FromSeconds(_periodSeconds);

    /// <summary>Whether codes are Steam's letters and digits, as KeePassXC's <c>encoder=steam</c> says.</summary>
    public bool IsSteam { get; }

    /// <summary>Whether every byte of the seed is zero. A test hook, so the zeroing is asserted rather than assumed.</summary>
    internal bool SeedIsZeroed => _seed.All(value => value == 0);

    /// <summary>The code current now, and how long it stays current.</summary>
    /// <param name="clock">The clock to read now from.</param>
    /// <returns>The code.</returns>
    /// <exception cref="ObjectDisposedException">The spec was disposed and its seed zeroed.</exception>
    public TotpCode Code(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return Code(clock.GetUtcNow());
    }

    /// <summary>The code current at a moment, and how long it stays current.</summary>
    /// <param name="at">The moment, no earlier than 1970-01-01 UTC.</param>
    /// <returns>The code.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="at"/> is before the Unix epoch.</exception>
    /// <exception cref="ObjectDisposedException">The spec was disposed and its seed zeroed.</exception>
    public TotpCode Code(DateTimeOffset at)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(at, DateTimeOffset.UnixEpoch);

        var elapsed = at.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var step = Period.Ticks;

        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, elapsed / step);

        Span<byte> mac = stackalloc byte[HMACSHA512.HashSizeInBytes];
        try
        {
            var length = Hmac(Algorithm, _seed, counter, mac);
            var offset = mac[length - 1] & 0x0F;
            var truncated = BinaryPrimitives.ReadInt32BigEndian(mac.Slice(offset, sizeof(int))) & 0x7FFF_FFFF;

            return new TotpCode(Encode(truncated), Remaining(Period, at));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(mac);
        }
    }

    /// <summary>How long the code current at a moment stays current, from the period alone, so a countdown reads no seed.</summary>
    /// <param name="period">How long each code is current, as <see cref="Period"/> gives it.</param>
    /// <param name="at">The moment, no earlier than 1970-01-01 UTC.</param>
    /// <returns>The time until the next period starts: more than zero and at most <paramref name="period"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="period"/> is not positive, or <paramref name="at"/> is before the Unix epoch.</exception>
    public static TimeSpan Remaining(TimeSpan period, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(at, DateTimeOffset.UnixEpoch);

        var step = period.Ticks;

        return TimeSpan.FromTicks(step - ((at.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) % step));
    }

    /// <summary>How codes are computed, never the seed.</summary>
    /// <returns>Such as <c>TOTP: Sha1, 6 digits every 30 s</c>.</returns>
    public override string ToString() => IsSteam
        ? $"TOTP: Steam, {Digits} characters every {_periodSeconds} s"
        : $"TOTP: {Algorithm}, {Digits} digits every {_periodSeconds} s";

    /// <summary>Zeroes the seed.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_seed);
        _disposed = true;
    }

    /// <summary>
    /// Whether a custom field is one a TOTP is read from: <c>otp</c>, <c>TOTP Seed</c>, <c>TOTP Settings</c>, or one
    /// starting <c>TimeOtp-</c>, matched exactly as KeePassXC and KeePass 2 do.
    /// </summary>
    /// <param name="name">The field's name.</param>
    /// <returns><see langword="true"/> when <see cref="Read"/> reads it.</returns>
    internal static bool IsSourceField(string name) =>
        string.Equals(name, OtpField, StringComparison.Ordinal)
        || string.Equals(name, _legacySeed, StringComparison.Ordinal)
        || string.Equals(name, _legacySettings, StringComparison.Ordinal)
        || name.StartsWith(_keePass2Prefix, StringComparison.Ordinal);

    /// <summary>Whether an item may have a TOTP, judged by which of its seed fields hold anything, so no value is read.</summary>
    /// <param name="holds">Whether the item's custom field of this name holds anything.</param>
    /// <returns>
    /// <see langword="true"/> when <c>otp</c>, <c>TOTP Seed</c>, <c>TOTP Settings</c> or one of KeePass 2's secret fields does;
    /// <see cref="Read"/> may still refuse it.
    /// </returns>
    internal static bool MayHold(Func<string, bool> holds)
    {
        ArgumentNullException.ThrowIfNull(holds);

        return holds(OtpField) || holds(_legacySeed) || holds(_legacySettings) || _keePass2Secrets.Any(holds);
    }

    /// <summary>
    /// An item's TOTP from its fields, in KeePassXC 2.7's order: <c>TOTP Settings</c> with <c>TOTP Seed</c> whenever the
    /// item has <c>TOTP Settings</c>, and none when both are empty; otherwise <c>otp</c> when it holds anything, otherwise
    /// KeePass 2's <c>TimeOtp-*</c>.
    /// </summary>
    /// <param name="fields">The item's custom fields by name; any but those <see cref="IsSourceField"/> accepts are ignored.</param>
    /// <param name="refusal">Why the item's TOTP cannot be computed, naming a field and never a value; null otherwise.</param>
    /// <returns>The spec, which the caller disposes; null when the item has none or it is refused.</returns>
    internal static TotpSpec? Read(Dictionary<string, string> fields, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var seed = fields.GetValueOrDefault(_legacySeed, string.Empty);

        // KeePassXC reads no other field once the item has TOTP Settings.
        if (fields.TryGetValue(_legacySettings, out var settings))
        {
            if (settings.Length == 0 && seed.Length == 0)
            {
                refusal = null;
                return null;
            }

            return FromSettings(settings, seed, out refusal);
        }

        if (fields.TryGetValue(OtpField, out var otp) && !string.IsNullOrWhiteSpace(otp))
        {
            return !IsUri(otp) && IsKeeOtp(otp) ? FromKeeOtp(otp, OtpField, out refusal) : FromUri(otp, out refusal);
        }

        return FromKeePass2(fields, out refusal);
    }

    /// <summary>A TOTP from KeePassXC's <c>otp</c> field: <c>otpauth://totp/[label]?secret=…</c>, with optional <c>algorithm</c>, <c>digits</c>, <c>period</c> and <c>encoder</c>.</summary>
    /// <param name="uri">The field's value.</param>
    /// <param name="refusal">Why it cannot be computed, naming a parameter and never a value; null otherwise.</param>
    /// <returns>The spec, or null when it is refused.</returns>
    /// <remarks>
    /// Parameter names match exactly, as KeePassXC's <c>QUrlQuery</c> matches them, so <c>Digits</c> is ignored as
    /// <c>issuer</c> is; a name with no <c>=</c> is given empty, and a parameter given twice is refused.
    /// </remarks>
    internal static TotpSpec? FromUri(string uri, out string? refusal) => FromUri(uri, OtpField, out refusal);

    /// <summary>
    /// A TOTP from KeeOTP's form, as KeePassXC 2.7 reads it in <c>otp</c> or <c>TOTP Settings</c>: <c>key=…</c>, the
    /// base32 seed, with optional <c>size</c>, <c>step</c>, <c>otpHashMode</c> and <c>type</c>.
    /// </summary>
    /// <param name="text">The field's value.</param>
    /// <param name="field">The field it was read from, which a refusal names.</param>
    /// <param name="refusal">Why it cannot be computed, naming a parameter and never a value; null otherwise.</param>
    /// <returns>The spec, or null when it is refused.</returns>
    /// <remarks>
    /// KeePassXC computes a TOTP whatever <c>type</c> says; keypaste refuses <c>type=hotp</c>, as it refuses an
    /// <c>otpauth://hotp/</c> URI, and any type but <c>totp</c>.
    /// </remarks>
    internal static TotpSpec? FromKeeOtp(string text, string field, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(field);

        var query = text.AsSpan().Trim();

        if (Parameters(query, _keeOtpParameters, field, out refusal) is not { } given)
        {
            return null;
        }

        if (!given.TryGetValue(_keeOtpKey, out var key) || query[key].IsWhiteSpace())
        {
            return Refuse(out refusal, $"The {field} field has no key.");
        }

        var type = Parameter(query, given, _keeOtpType);

        if (string.Equals(type, "hotp", StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(out refusal, $"The {field} field holds a counter-based HOTP, which keypaste does not compute.");
        }

        if (!string.IsNullOrEmpty(type) && !string.Equals(type, "totp", StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(out refusal, $"The {field} field's type must be totp.");
        }

        if (Named(Parameter(query, given, _keeOtpAlgorithm)) is not { } algorithm)
        {
            return Refuse(out refusal, $"The {field} field's otpHashMode must be SHA1, SHA256 or SHA512.");
        }

        if (!TryWhole(Parameter(query, given, _keeOtpSize), _defaultDigits, FewestDigits, MostDigits, out var digits))
        {
            return Refuse(out refusal, $"The {field} field's size must be 6, 7 or 8.");
        }

        if (!TryWhole(Parameter(query, given, _keeOtpStep), _defaultPeriod, 1, LongestPeriod, out var period))
        {
            return Refuse(out refusal, $"The {field} field's step must be a whole number of seconds from 1 to 86400.");
        }

        if (UriSecret(query[key]) is not { } seed)
        {
            return Refuse(out refusal, $"The {field} field's key is not base32.");
        }

        refusal = null;
        return new TotpSpec(seed, algorithm, digits, period, isSteam: false);
    }

    /// <summary>
    /// A TOTP from KeePassXC's older pair, as KeePassXC 2.7 reads it: <c>TOTP Settings</c> holding <c>period;digits</c>,
    /// or <c>period;S</c> for Steam's five characters every 30 seconds, with the base32 seed in <c>TOTP Seed</c>; or
    /// <c>TOTP Settings</c> holding an <c>otpauth://totp/</c> URI or KeeOTP's form, whose own seed is read instead.
    /// </summary>
    /// <param name="settings">The value of <c>TOTP Settings</c>; empty means 30 seconds and 6 digits.</param>
    /// <param name="seed">The value of <c>TOTP Seed</c>.</param>
    /// <param name="refusal">Why it cannot be computed, naming a field and never a value; null otherwise.</param>
    /// <returns>The spec, or null when it is refused.</returns>
    /// <remarks>Codes are HMAC-SHA-1, the only algorithm the pair can say; anything KeePassXC would read as another period or length is refused.</remarks>
    internal static TotpSpec? FromSettings(string settings, string seed, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(seed);

        if (IsUri(settings))
        {
            return FromUri(settings, _legacySettings, out refusal);
        }

        if (IsKeeOtp(settings))
        {
            return FromKeeOtp(settings, _legacySettings, out refusal);
        }

        if (string.IsNullOrWhiteSpace(seed))
        {
            return Refuse(out refusal, $"The {_legacySettings} field has no {_legacySeed}.");
        }

        var parts = settings.Split(';');
        var period = _defaultPeriod;
        var digits = _defaultDigits;
        var isSteam = false;

        if (parts.Length >= 2 && parts[1] == _steamShortName)
        {
            // KeePassXC computes Steam's codes every 30 seconds whatever period the pair names.
            if (!string.IsNullOrWhiteSpace(parts[0]) && !TryWhole(parts[0], _defaultPeriod, _defaultPeriod, _defaultPeriod, out _))
            {
                return Refuse(out refusal, $"The {_legacySettings} field gives Steam a period other than 30 seconds.");
            }

            digits = SteamDigits;
            isSteam = true;
        }
        else if (parts.Length >= 2)
        {
            if (!TryWhole(parts[0], _defaultPeriod, 1, LongestPeriod, out period))
            {
                return Refuse(out refusal, $"The {_legacySettings} field's period must be a whole number of seconds from 1 to 86400.");
            }

            if (!TryWhole(parts[1], _defaultDigits, FewestDigits, MostDigits, out digits))
            {
                return Refuse(out refusal, $"The {_legacySettings} field's digits must be 6, 7 or 8.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(settings))
        {
            return Refuse(out refusal, $"The {_legacySettings} field is not period;digits, period;S, an otpauth://totp/ URI or KeeOTP's key= form.");
        }

        if (Base32(seed) is not { } bytes)
        {
            return Refuse(out refusal, $"The {_legacySeed} field is not base32.");
        }

        refusal = null;
        return new TotpSpec(bytes, TotpAlgorithm.Sha1, digits, period, isSteam);
    }

    /// <summary>
    /// A TOTP from KeePass 2's fields: one of <c>TimeOtp-Secret</c> (UTF-8), <c>-Hex</c>, <c>-Base32</c> or
    /// <c>-Base64</c>, with optional <c>TimeOtp-Algorithm</c>, <c>TimeOtp-Length</c> and <c>TimeOtp-Period</c>.
    /// </summary>
    /// <param name="fields">The item's custom fields by name.</param>
    /// <param name="refusal">Why it cannot be computed, naming a field and never a value; null otherwise.</param>
    /// <returns>The spec, or null when the item has no secret field or it is refused.</returns>
    /// <remarks>An empty field counts as absent, as in KeePass 2; two secret fields are refused rather than one chosen.</remarks>
    internal static TotpSpec? FromKeePass2(Dictionary<string, string> fields, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(fields);

        string? secretField = null;

        foreach (var field in _keePass2Secrets)
        {
            if (Given(fields, field) is null)
            {
                continue;
            }

            if (secretField is not null)
            {
                return Refuse(out refusal, "The item has more than one TimeOtp-Secret field.");
            }

            secretField = field;
        }

        if (secretField is null)
        {
            refusal = null;
            return null;
        }

        TotpAlgorithm? algorithm = Given(fields, _algorithm) switch
        {
            null => TotpAlgorithm.Sha1,
            { } named when named.Equals("HMAC-SHA-1", StringComparison.OrdinalIgnoreCase) => TotpAlgorithm.Sha1,
            { } named when named.Equals("HMAC-SHA-256", StringComparison.OrdinalIgnoreCase) => TotpAlgorithm.Sha256,
            { } named when named.Equals("HMAC-SHA-512", StringComparison.OrdinalIgnoreCase) => TotpAlgorithm.Sha512,
            _ => null,
        };

        if (algorithm is null)
        {
            return Refuse(out refusal, "TimeOtp-Algorithm must be HMAC-SHA-1, HMAC-SHA-256 or HMAC-SHA-512.");
        }

        if (!TryWhole(Given(fields, _length), _defaultDigits, FewestDigits, MostDigits, out var digits))
        {
            return Refuse(out refusal, "TimeOtp-Length must be 6, 7 or 8.");
        }

        if (!TryWhole(Given(fields, _period), _defaultPeriod, 1, LongestPeriod, out var period))
        {
            return Refuse(out refusal, "TimeOtp-Period must be a whole number of seconds from 1 to 86400.");
        }

        var value = fields[secretField];
        var (seed, encoding) = secretField switch
        {
            _secretHex => (Hex(value), "hex"),
            _secretBase32 => (Base32(value), "base32"),
            _secretBase64 => (Base64(value), "base64"),
            _ => (Encoding.UTF8.GetBytes(value), "UTF-8"),
        };

        if (seed is null)
        {
            return Refuse(out refusal, $"{secretField} is not {encoding}.");
        }

        refusal = null;
        return new TotpSpec(seed, algorithm.Value, digits, period, isSteam: false);
    }

    /// <summary>RFC 4648 base32 in either case, ignoring spaces and trailing padding.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The bytes, which the caller zeroes; null when the text is empty, holds another character or ends in a group no encoder writes.</returns>
    internal static byte[]? Base32(ReadOnlySpan<char> text)
    {
        var length = 0;
        var padded = false;

        foreach (var c in text)
        {
            if (c == ' ')
            {
                continue;
            }

            if (c == '=')
            {
                padded = true;
                continue;
            }

            if (padded || Base32Value(c) < 0)
            {
                return null;
            }

            length++;
        }

        if (length == 0 || (length % 8) is 1 or 3 or 6)
        {
            return null;
        }

        var bytes = new byte[length * 5 / 8];
        var buffer = 0;
        var bits = 0;
        var written = 0;

        foreach (var c in text)
        {
            var value = Base32Value(c);
            if (value < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                bytes[written++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }

        return bytes;
    }

    private static int Base32Value(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a',
        >= '2' and <= '7' => c - '2' + 26,
        _ => -1,
    };

    private static byte[]? Hex(ReadOnlySpan<char> text)
    {
        var digits = new char[text.Length];
        var length = 0;

        try
        {
            foreach (var c in text)
            {
                if (c != ' ')
                {
                    digits[length++] = c;
                }
            }

            if (length == 0 || length % 2 != 0)
            {
                return null;
            }

            var bytes = new byte[length / 2];

            if (Convert.FromHexString(digits.AsSpan(0, length), bytes, out _, out _) == OperationStatus.Done)
            {
                return bytes;
            }

            CryptographicOperations.ZeroMemory(bytes);
            return null;
        }
        finally
        {
            Array.Clear(digits);
        }
    }

    private static byte[]? Base64(ReadOnlySpan<char> text)
    {
        var decoded = new byte[(text.Length + 3) / 4 * 3];

        try
        {
            return Convert.TryFromBase64Chars(text, decoded, out var written) && written > 0
                ? decoded.AsSpan(0, written).ToArray()
                : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static byte[]? UriSecret(ReadOnlySpan<char> escaped)
    {
        var text = new char[escaped.Length];

        try
        {
            return Uri.TryUnescapeDataString(escaped, text, out var written) ? Base32(text.AsSpan(0, written)) : null;
        }
        finally
        {
            Array.Clear(text);
        }
    }

    private static TotpSpec? FromUri(string uri, string field, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var text = uri.AsSpan().Trim();

        if (!text.StartsWith(_scheme, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(out refusal, $"The {field} field is not an otpauth://totp/ URI.");
        }

        text = text[_scheme.Length..];

        var fragment = text.IndexOf('#');
        if (fragment >= 0)
        {
            text = text[..fragment];
        }

        var question = text.IndexOf('?');
        var path = question >= 0 ? text[..question] : text;
        var query = question >= 0 ? text[(question + 1)..] : ReadOnlySpan<char>.Empty;
        var slash = path.IndexOf('/');
        var type = slash >= 0 ? path[..slash] : path;

        if (type.Equals("hotp", StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(out refusal, $"The {field} field holds a counter-based HOTP, which keypaste does not compute.");
        }

        if (!type.Equals("totp", StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(out refusal, $"The {field} field is not an otpauth://totp/ URI.");
        }

        if (Parameters(query, _uriParameters, field, out refusal) is not { } given)
        {
            return null;
        }

        if (!given.TryGetValue(_uriSecret, out var secret) || query[secret].IsWhiteSpace())
        {
            return Refuse(out refusal, $"The {field} field has no secret.");
        }

        if (Named(Parameter(query, given, _uriAlgorithm)) is not { } algorithm)
        {
            return Refuse(out refusal, $"The {field} field's algorithm must be SHA1, SHA256 or SHA512.");
        }

        bool? isSteam = Parameter(query, given, _uriEncoder) switch
        {
            null or "" => false,
            _steamEncoder => true,
            _ => null,
        };

        if (isSteam is null)
        {
            return Refuse(out refusal, $"The {field} field's encoder must be steam, the only one keypaste computes.");
        }

        var digitsText = Parameter(query, given, _uriDigits);

        int digits;
        if (isSteam.Value)
        {
            // Without digits KeePassXC 2.7.10 onward computes six of Steam's characters and earlier releases five.
            if (digitsText is null || !TryWhole(digitsText, SteamDigits, SteamDigits, SteamDigits, out digits))
            {
                return Refuse(out refusal, $"The {field} field's digits must be 5 for Steam.");
            }
        }
        else if (!TryWhole(digitsText, _defaultDigits, FewestDigits, MostDigits, out digits))
        {
            return Refuse(out refusal, $"The {field} field's digits must be 6, 7 or 8.");
        }

        if (!TryWhole(Parameter(query, given, _uriPeriod), _defaultPeriod, 1, LongestPeriod, out var period))
        {
            return Refuse(out refusal, $"The {field} field's period must be a whole number of seconds from 1 to 86400.");
        }

        // KeePassXC before 2.7.10 computes Steam's codes every 30 seconds whatever period the URI names.
        if (isSteam.Value && period != _defaultPeriod)
        {
            return Refuse(out refusal, $"The {field} field gives Steam a period other than 30 seconds.");
        }

        if (UriSecret(query[secret]) is not { } seed)
        {
            return Refuse(out refusal, $"The {field} field's secret is not base32.");
        }

        refusal = null;
        return new TotpSpec(seed, algorithm, digits, period, isSteam.Value);
    }

    /// <summary>The parameters of a query that a reader knows, by the exact name KeePassXC's <c>QUrlQuery</c> matches; a name with no <c>=</c> is given empty.</summary>
    private static Dictionary<string, Range>? Parameters(ReadOnlySpan<char> query, string[] known, string field, out string? refusal)
    {
        Dictionary<string, Range> given = new(StringComparer.Ordinal);

        foreach (var range in query.Split('&'))
        {
            var (offset, length) = range.GetOffsetAndLength(query.Length);
            var part = query.Slice(offset, length);
            var equals = part.IndexOf('=');
            var end = offset + length;

            if (Known(equals < 0 ? part : part[..equals], known) is not { } name)
            {
                continue;
            }

            if (!given.TryAdd(name, equals < 0 ? new Range(end, end) : new Range(offset + equals + 1, end)))
            {
                refusal = $"The {field} field gives its {name} more than once.";
                return null;
            }
        }

        refusal = null;
        return given;
    }

    private static string? Known(ReadOnlySpan<char> name, string[] known)
    {
        foreach (var candidate in known)
        {
            if (name.Equals(candidate, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? Parameter(ReadOnlySpan<char> query, Dictionary<string, Range> given, string name) =>
        given.TryGetValue(name, out var range) ? Uri.UnescapeDataString(query[range]) : null;

    /// <summary>
    /// An algorithm as every KeePassXC 2.7 release names one in a URI or KeeOTP's form, in any case; absent or empty
    /// is SHA-1, as there. An <c>HMAC-SHA-</c> name is refused: 2.7.10 onward reads it and earlier releases compute SHA-1.
    /// </summary>
    private static TotpAlgorithm? Named(string? name) => name switch
    {
        null or "" => TotpAlgorithm.Sha1,
        { } named when named.Equals("SHA1", StringComparison.OrdinalIgnoreCase) => TotpAlgorithm.Sha1,
        { } named when named.Equals("SHA256", StringComparison.OrdinalIgnoreCase) => TotpAlgorithm.Sha256,
        { } named when named.Equals("SHA512", StringComparison.OrdinalIgnoreCase) => TotpAlgorithm.Sha512,
        _ => null,
    };

    private static bool IsUri(string text) => text.AsSpan().TrimStart().StartsWith(_schemeName, StringComparison.OrdinalIgnoreCase);

    private static bool IsKeeOtp(string text)
    {
        var query = text.AsSpan().Trim();

        foreach (var range in query.Split('&'))
        {
            var part = query[range];
            var equals = part.IndexOf('=');

            if ((equals < 0 ? part : part[..equals]).Equals(_keeOtpKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Given(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    /// <summary>A whole number in a range; <paramref name="text"/> null is absent and takes <paramref name="absent"/>, while given empty is refused, as KeePassXC reads it as 0.</summary>
    private static bool TryWhole(string? text, int absent, int least, int most, out int value)
    {
        if (text is null)
        {
            value = absent;
            return true;
        }

        return int.TryParse(text.AsSpan().Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value >= least
            && value <= most;
    }

    private static int Hmac(TotpAlgorithm algorithm, ReadOnlySpan<byte> key, ReadOnlySpan<byte> message, Span<byte> mac)
    {
        switch (algorithm)
        {
            case TotpAlgorithm.Sha256:
                return HMACSHA256.HashData(key, message, mac);
            case TotpAlgorithm.Sha512:
                return HMACSHA512.HashData(key, message, mac);
            default:
#pragma warning disable CA5350 // RFC 6238's default, chosen by whoever issued the seed; keypaste computes the code KeePassXC shows.
                return HMACSHA1.HashData(key, message, mac);
#pragma warning restore CA5350
        }
    }

    private static TotpSpec? Refuse(out string? refusal, string reason)
    {
        refusal = reason;
        return null;
    }

    private string Encode(int truncated)
    {
        var alphabet = IsSteam ? _steam : _decimal;
        Span<char> code = stackalloc char[Digits];

        for (var i = 0; i < Digits; i++)
        {
            code[IsSteam ? i : Digits - 1 - i] = alphabet[truncated % alphabet.Length];
            truncated /= alphabet.Length;
        }

        return new string(code);
    }
}
