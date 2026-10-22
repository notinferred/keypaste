using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace Keypaste.AppDriver;

/// <summary>
/// Writes the Twofish vault <c>scripts/verify-keepassxc-workflows.sh</c> starts its Twofish steps from, with the vendored
/// library alone, because <c>keepassxc-cli</c> cannot choose a cipher and keypaste never chooses one (D-0292).
/// </summary>
/// <remarks>
/// It is the vault KeePass with its Twofish plugin writes: Twofish under AES-KDF, holding <c>env/tf/deploy</c> tagged
/// <c>env:tf</c> with a protected <c>TOKEN</c> set to <c>KEYPASTE_DRIVER_NEW_PASSWORD</c>. The gate has the shipped CLI
/// save it before KeePassXC reads it.
/// </remarks>
internal static class TwofishVault
{
    /// <summary>Writes the vault at a path that holds no file.</summary>
    /// <param name="path">Where to write it.</param>
    /// <returns>0 when written, 3 when a file is already there.</returns>
    internal static int Create(string path)
    {
        if (File.Exists(path))
        {
            Console.Error.WriteLine($"driver could not arrange the act: {path} already exists");
            return 3;
        }

        var password = Environment.GetEnvironmentVariable("KEYPASTE_DRIVER_PASSWORD") ?? string.Empty;
        var token = Environment.GetEnvironmentVariable("KEYPASTE_DRIVER_NEW_PASSWORD") ?? string.Empty;

        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(password));

        PwDatabase database = new();
        try
        {
            database.New(IOConnectionInfo.FromPath(path), key);
            database.DataCipherUuid = TwofishCipherEngine.TwofishUuid;
            database.KdfParameters = new AesKdf().GetDefaultParameters();
            database.KdfParameters.SetUInt64(AesKdf.ParamRounds, 1000);

            PwGroup env = new(true, true, "env", PwIcon.Folder);
            database.RootGroup.AddGroup(env, true);
            PwGroup project = new(true, true, "tf", PwIcon.Folder);
            env.AddGroup(project, true);

            PwEntry deploy = new(true, true);
            deploy.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "deploy"));
            deploy.Strings.Set("TOKEN", new ProtectedString(true, token));
            deploy.AddTag("env:tf");
            project.AddEntry(deploy, true);

            database.Save(null);
        }
        finally
        {
            database.Close();
        }

        Console.WriteLine("written");
        return 0;
    }
}
