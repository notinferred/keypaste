/*
  keypaste addition to the vendored KeePassLib, compiled only under KEYPASTE_CHALLENGE_RESPONSE.
  See third_party/KeePassLib/UPSTREAM.md.
*/

#if KEYPASTE_CHALLENGE_RESPONSE
namespace KeePassLib.Keys
{
	/// <summary>
	/// A user key whose contribution is a device's answer to a challenge the file
	/// supplies, as KeePassXC's challenge-response keys are. Its <c>KeyData</c> is
	/// <c>null</c>: <c>CompositeKey</c> asks it for a response instead and folds the
	/// SHA-256 of every response into the key.
	/// </summary>
	public interface IChallengeResponseUserKey : IUserKey
	{
		/// <summary>
		/// The device's raw response to <paramref name="pbChallenge" />. Throws when
		/// there is none, so no key is ever derived without it.
		/// </summary>
		byte[] GetResponse(byte[] pbChallenge);
	}
}
#endif
