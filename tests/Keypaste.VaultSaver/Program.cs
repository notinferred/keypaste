using Keypaste.Core;

namespace Keypaste.VaultSaver;

/// <summary>Saves one vault, once, at a given retry budget, and says what happened.</summary>
/// <remarks>
/// <para>
/// V-F.6's saver, and the reason it is a process rather than a method. The product redirects its
/// temporary directory once, on its first save, and then relies on <c>TMP</c> staying where it put
/// it. A test that set <c>TMP</c> in its own process after that had already happened would not
/// mislead the test so much as undo the fix — <c>TxfPrepare</c> reads <c>TMP</c> at save time, so
/// the save would go straight back into the contended directory and fail on a build that is
/// repaired.
/// </para>
/// <para>
/// Launched with <c>TMP</c> already in its environment, this process meets that value on the way
/// through the same startup path a shipped binary takes: with the fix, the redirect fires on it and
/// puts the private directory inside it; without, the save names its temporary in it directly. No
/// test-only setup either way, which is what makes a green run mean something.
/// </para>
/// <para>
/// The master password arrives in the environment rather than in <c>argv</c>. It is a fixture value
/// and not a real secret, but a command line is readable by every process on the machine and this
/// repository does not write code that reads as though that were fine.
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var attempts))
        {
            Console.Error.WriteLine("usage: <vault-path> <attempts>   (password in KEYPASTE_SAVER_PASSWORD)");
            return 2;
        }

        var password = Environment.GetEnvironmentVariable("KEYPASTE_SAVER_PASSWORD");

        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("KEYPASTE_SAVER_PASSWORD is unset");
            return 2;
        }

        // Printed before anything opens a vault, because it is the whole premise: this is the
        // ambient temporary directory the product is about to decide what to do with.
        Console.WriteLine($"ambient TMP   {Environment.GetEnvironmentVariable("TMP")}");
        Console.WriteLine($"temp path     {Path.GetTempPath()}");

        try
        {
            using var vault = Vault.Open(args[0], password);
            vault.AddEntry(new VaultEntry { Title = "written under contention", Password = "secret" });
            vault.SaveWaiting(waitBetweenAttempts: null, attempts: attempts);

            // After the save, so it names the directory the save actually used.
            Console.WriteLine($"saved into    {Path.GetTempPath()}");
            return 0;
        }
        catch (VaultException ex)
        {
            Console.Error.WriteLine($"save refused: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"saver failed: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }
}
