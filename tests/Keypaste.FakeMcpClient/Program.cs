using System.Text.Json;
using System.Text.Json.Nodes;

namespace Keypaste.FakeMcpClient;

/// <summary>
/// Answers Claude Code's <c>--version</c> and <c>mcp add</c>, <c>remove</c> and <c>list</c> as it
/// does, keeping its servers in the file <c>KEYPASTE_FAKE_CLIENT_CONFIG</c> names.
/// </summary>
/// <remarks>
/// <c>scripts/verify-connect-client.sh</c> puts it on PATH so the desktop's Connect registers with a
/// client whose configuration the gate can read afterwards, and never with a real one. It refuses to
/// run without that variable, so finding it on PATH by accident changes nothing. Adding a name that
/// exists is refused and removing one that does not is refused, as Claude Code refuses them.
/// </remarks>
internal static class Program
{
    private const string _variable = "KEYPASTE_FAKE_CLIENT_CONFIG";

    private static int Main(string[] args)
    {
        var path = Environment.GetEnvironmentVariable(_variable);
        if (string.IsNullOrEmpty(path))
        {
            Console.Error.WriteLine($"{_variable} is unset; this stand-in client writes nowhere else");
            return 2;
        }

        var config = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject();
        var servers = config["mcpServers"] as JsonObject ?? [];
        config["mcpServers"] = servers;

        switch (Options(args))
        {
            case ["--version"]:
                Console.Out.WriteLine("0.0.0 (keypaste stand-in for Claude Code)");
                return 0;

            case ["mcp", "add", var name, "--", var command, .. var rest]:
                if (servers.ContainsKey(name))
                {
                    Console.Error.WriteLine($"MCP server {name} already exists in user config");
                    return 1;
                }

                servers[name] = new JsonObject { ["command"] = command, ["args"] = new JsonArray([.. rest.Select(arg => (JsonNode)arg)]) };
                break;

            case ["mcp", "remove", var name]:
                if (!servers.Remove(name))
                {
                    Console.Error.WriteLine($"No MCP server found with name: {name}");
                    return 1;
                }

                break;

            case ["mcp", "list"]:
                if (servers.Count == 0)
                {
                    Console.Out.WriteLine("No MCP servers configured.");
                }

                foreach (var (name, server) in servers)
                {
                    var line = new[] { (string)server!["command"]! }.Concat(server["args"]!.AsArray().Select(arg => (string)arg!));
                    Console.Out.WriteLine($"{name}: {string.Join(' ', line)}");
                }

                return 0;

            default:
                Console.Error.WriteLine($"the stand-in does not understand: {string.Join(' ', args)}");
                return 2;
        }

        File.WriteAllText(path, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>The arguments without the scope and transport flags, which only Claude Code's own add and remove take.</summary>
    private static string[] Options(string[] args)
    {
        var separator = Array.IndexOf(args, "--");
        var flags = separator < 0 ? args : args[..separator];
        List<string> kept = [];

        for (var i = 0; i < flags.Length; i++)
        {
            if (flags[i] is "--scope" or "--transport" && i + 1 < flags.Length)
            {
                i++;
                continue;
            }

            kept.Add(flags[i]);
        }

        return separator < 0 ? [.. kept] : [.. kept, .. args[separator..]];
    }
}
