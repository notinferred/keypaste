using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The credential path's <see cref="EntryName"/>: the type that makes "return ONLY the requested
/// field" structural rather than a rule a code path could later break (THREATS.md T-8).
/// </summary>
public sealed class ReleasedFieldTests
{
    [Fact]
    public void ItCarriesTheFieldNameAndTheCharacters()
    {
        using var released = new ReleasedField("password", "sk_live_x");

        Assert.Equal("password", released.Field, StringComparer.Ordinal);
        Assert.Equal("sk_live_x", released.Value.ToString(), StringComparer.Ordinal);
        Assert.Equal(9, released.Length);
    }

    /// <summary>
    /// The grant cache zeroes an expired grant, and "expired" has to mean the characters are gone
    /// rather than merely unreachable — otherwise expiry is a promise instead of a fact.
    /// </summary>
    [Fact]
    public void DisposingZeroesTheReleasedCharacters()
    {
        var released = new ReleasedField("password", "sk_live_x");

        Assert.False(released.IsZeroed);

        released.Dispose();

        Assert.True(released.IsZeroed);
        Assert.True(released.Value.IsEmpty);
    }

    [Fact]
    public void ALongValueIsHeldWhole()
    {
        var value = new string('x', SecretBuffer.InitialCapacity * 5);

        using var released = new ReleasedField("notes", value);

        Assert.Equal(value, released.Value.ToString(), StringComparer.Ordinal);
    }

    [Fact]
    public void ItRejectsANullFieldName() =>
        Assert.Throws<ArgumentNullException>(() => new ReleasedField(null!, "x"));
}

/// <summary>
/// The list of releasable fields is a product rule and lives in one place, because the tool schema,
/// the server's re-validation, the approval prompt and <c>keypaste log</c> all have to agree about
/// it (CORE.md law 4.3).
/// </summary>
public sealed class CredentialFieldsTests
{
    [Fact]
    public void TheReleasableFields_AreExactlyTheFour()
    {
        Assert.Equal(["password", "username", "url", "notes"], CredentialFields.All);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("username")]
    [InlineData("url")]
    [InlineData("notes")]
    public void EachOfThemIsReleasable(string field) =>
        Assert.True(CredentialFields.IsReleasable(field));

    /// <summary>
    /// Case-sensitivity is the point: the schema spells these in lower case and the server
    /// re-validates against the same list, so accepting <c>Password</c> here would only widen what
    /// one half of that pair believes is legal.
    /// </summary>
    [Theory]
    [InlineData("Password")]
    [InlineData("PASSWORD")]
    [InlineData("")]
    [InlineData(" password")]
    [InlineData("totp")]
    [InlineData("recovery-codes")]
    public void NothingElseIs(string field) =>
        Assert.False(CredentialFields.IsReleasable(field));

    [Fact]
    public void IsReleasable_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => CredentialFields.IsReleasable(null!));
}
