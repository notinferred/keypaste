namespace Keypaste.Cli.Prompting;

/// <summary>Reads environment variables. A seam so tests never mutate the real environment.</summary>
internal interface IEnvironmentProbe
{
    /// <summary>The value of a variable, or null when unset.</summary>
    string? Get(string name);

    /// <summary>Every variable in the current process's environment.</summary>
    /// <remarks>
    /// <c>keypaste run</c> builds its child's environment explicitly instead of letting
    /// <c>ProcessStartInfo</c> inherit one implicitly, because a merge nobody can see is a merge
    /// nobody can test — and this one decides which credential a program receives.
    /// </remarks>
    IReadOnlyDictionary<string, string> All();
}

/// <summary>Reads the process's real environment.</summary>
internal sealed class SystemEnvironmentProbe : IEnvironmentProbe
{
    /// <inheritdoc/>
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> All()
    {
        // Filled by indexer rather than through the dictionary copy constructor: on Windows the
        // comparer is case-insensitive, and a parent block that happens to hold two names
        // differing only in case would make the constructor throw on a duplicate key.
        var values = new Dictionary<string, string>(Execution.EnvironmentMerge.Comparer);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                values[name] = value;
            }
        }

        return values;
    }
}
