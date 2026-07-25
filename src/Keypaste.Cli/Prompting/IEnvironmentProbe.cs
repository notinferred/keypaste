namespace Keypaste.Cli.Prompting;

/// <summary>Reads environment variables. A seam so tests never mutate the real environment.</summary>
internal interface IEnvironmentProbe
{
    /// <summary>The value of a variable, or null when unset.</summary>
    string? Get(string name);
}

/// <summary>Reads the process's real environment.</summary>
internal sealed class SystemEnvironmentProbe : IEnvironmentProbe
{
    /// <inheritdoc/>
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}
