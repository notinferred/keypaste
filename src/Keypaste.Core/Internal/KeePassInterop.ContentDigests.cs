extern alias KeePass;

using KeePass::KeePassLib;
using KeePass::KeePassLib.Collections;
using KeePass::KeePassLib.Security;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>Keyed digests of entries' and groups' content, equal where KeePassLib's merge finds the content equal.</summary>
    /// <remarks>
    /// An entry is compared as <c>EqualsEntry</c> compares it with <c>IgnoreParentGroup</c>, <c>IgnoreLastAccess</c>, <c>IgnoreHistory</c>
    /// and <c>NullEmptyEquivStd</c>, times in whole seconds, and a group by what <c>AssignProperties</c> copies but its access times. Each
    /// attachment enters as its SHA-256, taken once per instance. Custom data's own times, which KeePassLib keeps internal, the stamp KeePassXC rewrites on every save, keypaste's save count, which a merge sets itself, and a merge's mark on a version it kept are left out.
    /// </remarks>
    private sealed class ContentDigests(byte[] key) : IDisposable
    {
        private readonly IncrementalHash _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        private readonly Dictionary<ProtectedBinary, byte[]> _attachments = new(ReferenceEqualityComparer.Instance);

        internal byte[] Of(PwEntry entry) => Digest(entry, customData: true);

        /// <summary>An entry's digest without its custom data, which KeePassXC leaves out of the versions it keeps in history.</summary>
        internal byte[] OfHistoryCopy(PwEntry entry) => Digest(entry, customData: false);

        private byte[] Digest(PwEntry entry, bool customData)
        {
            _hmac.AppendData("entry"u8);
            Add(entry.Uuid);

            List<KeyValuePair<string, ProtectedString>> fields =
            [
                .. entry.Strings
                    .Where(field => !(field.Value.IsEmpty && PwDefs.IsStandardField(field.Key)))
                    .OrderBy(field => field.Key, StringComparer.Ordinal),
            ];
            Add(fields.Count);

            foreach ((string name, ProtectedString value) in fields)
            {
                Add(name);
                byte[] utf8 = value.ReadUtf8();

                try
                {
                    Add(utf8);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(utf8);
                }
            }

            List<KeyValuePair<string, ProtectedBinary>> attachments = [.. entry.Binaries.OrderBy(attachment => attachment.Key, StringComparer.Ordinal)];
            Add(attachments.Count);

            foreach ((string name, ProtectedBinary content) in attachments)
            {
                Add(name);
                Add(content.IsProtected);
                _hmac.AppendData(DigestOf(content));
            }

            Add(entry.AutoType.Enabled);
            Add((long)entry.AutoType.ObfuscationOptions);
            Add(entry.AutoType.DefaultSequence);
            Add(entry.AutoType.AssociationsCount);

            foreach (AutoTypeAssociation association in entry.AutoType.Associations)
            {
                Add(association.WindowName);
                Add(association.Sequence);
            }

            Add((long)entry.IconId);
            Add(entry.CustomIconUuid);
            Add(entry.ForegroundColor.IsEmpty);
            Add(entry.ForegroundColor.ToArgb());
            Add(entry.BackgroundColor.IsEmpty);
            Add(entry.BackgroundColor.ToArgb());
            Add(Second(entry.CreationTime));
            Add(Second(entry.LastModificationTime));
            Add(Second(entry.ExpiryTime));
            Add(entry.Expires);
            Add(entry.OverrideUrl);
            Add(entry.QualityCheck);
            Add(TagsOf(entry));
            if (customData)
            {
                Add(entry.CustomData);
            }

            return _hmac.GetHashAndReset();
        }

        internal byte[] Of(PwGroup group)
        {
            _hmac.AppendData("group"u8);
            Add(group.Uuid);
            Add(group.Name);
            Add(group.Notes);
            Add((long)group.IconId);
            Add(group.CustomIconUuid);
            Add(Second(group.CreationTime));
            Add(Second(group.LastModificationTime));
            Add(Second(group.ExpiryTime));
            Add(group.Expires);
            Add(group.DefaultAutoTypeSequence);
            Add(group.EnableAutoType);
            Add(group.EnableSearching);
            Add(group.LastTopVisibleEntry);
            Add(TagsOf(group));
            Add(group.CustomData);
            return _hmac.GetHashAndReset();
        }

        public void Dispose() => _hmac.Dispose();

        private byte[] DigestOf(ProtectedBinary content)
        {
            if (!_attachments.TryGetValue(content, out byte[]? digest))
            {
                byte[] bytes = content.ReadData();

                try
                {
                    digest = SHA256.HashData(bytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }

                _attachments[content] = digest;
            }

            return digest;
        }

        private void Add(ReadOnlySpan<byte> bytes)
        {
            Add((long)bytes.Length);
            _hmac.AppendData(bytes);
        }

        private void Add(string text) => Add(Encoding.UTF8.GetBytes(text));

        private void Add(long number)
        {
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, number);
            _hmac.AppendData(bytes);
        }

        private void Add(bool flag) => Add(flag ? 1L : 0L);

        private void Add(bool? flag) => Add(flag switch { null => 2L, true => 1L, false => 0L });

        private void Add(PwUuid id) => _hmac.AppendData(id.UuidBytes);

        private void Add(List<string> tags)
        {
            Add((long)tags.Count);

            foreach (string tag in tags)
            {
                Add(tag);
            }
        }

        private void Add(StringDictionaryEx data)
        {
            List<KeyValuePair<string, string>> items =
            [
                .. data.Where(item => item.Key is not (PwEntry.KeptVersionKey or _keePassXcStamp or _saveCountKey))
                    .OrderBy(item => item.Key, StringComparer.Ordinal),
            ];
            Add((long)items.Count);

            foreach ((string name, string value) in items)
            {
                Add(name);
                Add(value);
            }
        }
    }
}
