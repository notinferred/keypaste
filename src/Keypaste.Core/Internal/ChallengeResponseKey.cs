extern alias KeePass;

using KeePass::KeePassLib.Keys;
using KeePass::KeePassLib.Security;
using Keypaste.Core.HardwareKeys;

namespace Keypaste.Core.Internal;

/// <summary>A hardware key as KeePassLib takes one: no key data of its own, only an answer to the file's challenge.</summary>
/// <remarks>
/// The vendored <c>CompositeKey</c> folds the SHA-256 of the answer into the key where KeePassXC
/// does (third_party/KeePassLib/UPSTREAM.md, <c>KEYPASTE_CHALLENGE_RESPONSE</c>).
/// </remarks>
internal sealed class ChallengeResponseKey(HardwareKey factor) : IChallengeResponseUserKey
{
    internal HardwareKey Factor { get; } = factor;

    public ProtectedBinary KeyData => null!;

    public byte[] GetResponse(byte[] pbChallenge) => Factor.Respond(pbChallenge);
}
