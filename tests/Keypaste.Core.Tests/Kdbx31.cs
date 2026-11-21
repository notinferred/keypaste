using System.Reflection;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace Keypaste.Core.Tests;

/// <summary>A vault file saved again as KDBX 3.1 under AES-KDF, as KeePass before 2.35 leaves it, through the vendored library's internal version switch.</summary>
internal static class Kdbx31
{
    internal static void SaveAs(string path, string masterPassword)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(masterPassword), false));

        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(path), key, null);
            database.DataCipherUuid = StandardAesEngine.AesUuid;
            database.KdfParameters = new AesKdf().GetDefaultParameters();
            database.KdfParameters.SetUInt64(AesKdf.ParamRounds, 1000);

            KdbxFile writer = new(database);
            typeof(KdbxFile).GetProperty("ForceVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(writer, 0x00030001u);

            using var stream = File.Create(path);
            writer.Save(stream, null, KeePassLib.Serialization.KdbxFormat.Default, null);
        }
        finally
        {
            database.Close();
        }
    }
}
