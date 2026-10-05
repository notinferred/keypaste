using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// No package the bridge brings into <c>keypaste</c> has a module initializer, so none of its code runs in
/// a process that does not start the bridge, such as <c>keypaste agent</c> holding the unlocked vault
/// (PRODUCT §3.9, D-0419).
/// </summary>
/// <remarks>
/// A module initializer runs when its module loads, at startup under NativeAOT, whether or not anything
/// uses the module; a static constructor runs only when its type is first used, which no CLI file but
/// <c>Program.cs</c> can do (<see cref="CliDispatchSourceRulesTests"/>). The closure is read from the CLI's
/// lock file, from the bridge's project down, so a package added to it is checked without editing this test.
/// </remarks>
public sealed class BridgeClosureTests
{
    [Fact]
    public void No_package_the_bridge_brings_has_a_module_initializer()
    {
        var closure = BridgeClosure();

        Assert.Contains("ModelContextProtocol.Core", closure);
        Assert.All(closure, package =>
        {
            var assembly = Path.Combine(AppContext.BaseDirectory, package + ".dll");
            Assert.True(File.Exists(assembly), $"{package}.dll is not beside the tests, so it was not checked");
            Assert.False(HasModuleInitializer(assembly), $"{package} has a module initializer, which would run in every keypaste process");
        });
    }

    [Fact]
    public void A_module_initializer_is_found()
    {
        Assert.True(HasModuleInitializer(typeof(BridgeClosureTests).Assembly.Location));
    }

    private static bool HasModuleInitializer(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var module = metadata.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(1));

        return module.GetMethods().Any(method => metadata.GetString(metadata.GetMethodDefinition(method).Name) == ".cctor");
    }

    private static List<string> BridgeClosure()
    {
        using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "src", "Keypaste.Cli", "packages.lock.json")));
        var entries = lockFile.RootElement.GetProperty("dependencies").GetProperty("net10.0")
            .EnumerateObject()
            .ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        List<string> closure = [];
        Queue<string> pending = new(Dependencies(entries["keypaste.mcp"]));

        while (pending.TryDequeue(out var name))
        {
            var entry = entries[name];

            if (entry.GetProperty("type").GetString() == "Project" || closure.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            closure.Add(name);

            foreach (var dependency in Dependencies(entry))
            {
                pending.Enqueue(dependency);
            }
        }

        return closure;
    }

    private static IEnumerable<string> Dependencies(JsonElement entry) =>
        entry.TryGetProperty("dependencies", out var dependencies)
            ? [.. dependencies.EnumerateObject().Select(dependency => dependency.Name)]
            : [];

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new Xunit.Sdk.XunitException("keypaste.slnx not found above the test's base directory");
        }

        return directory;
    }
}
