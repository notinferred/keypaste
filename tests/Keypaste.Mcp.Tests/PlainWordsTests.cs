using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Keypaste.Core.Audit;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// What an agent is told says item, what it can see, environment and allowance, never the words
/// those replaced (D-0457).
/// </summary>
/// <remarks>
/// Tool, argument and structured-result names, flags, file names, environment variables and backticked
/// names keep their spelling, since an agent types them, so they are taken out before looking.
/// </remarks>
public sealed partial class PlainWordsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"\b(grants?|granted|entry|entries|exposure|exposed|expose|sessions?|kdbx|stdio|profiles?|clients?)", RegexOptions.IgnoreCase)]
    private static partial Regex Retired();

    [GeneratedRegex(@"`[^`]*`|--[a-z][a-z-]*|\b[a-z]+(?:_[a-z]+)+\b|\b[\w-]+\.(?:toml|jsonl?|kdbx)\b|\bKEYPASTE_[A-Z_]+\b|kp://\S*")]
    private static partial Regex Interface();

    [Fact]
    public async Task WhatTheServerSays_OverARealSession_HoldsNoneOfTheRetiredWords()
    {
        await using var harness = new McpHarness();
        harness.Source.With("env/dev", "STRIPE_KEY");
        var client = await harness.StartAsync("--allow-run");

        List<(string Where, string Text)> said = [("instructions", client.ServerInstructions ?? string.Empty)];

        foreach (var tool in await client.ListToolsAsync(cancellationToken: Token))
        {
            said.Add(($"{tool.Name} description", tool.ProtocolTool.Description ?? string.Empty));
            said.Add(($"{tool.Name} title", tool.ProtocolTool.Title ?? string.Empty));
            said.Add(($"{tool.Name} annotation title", tool.ProtocolTool.Annotations?.Title ?? string.Empty));
            said.AddRange(Descriptions(tool.ProtocolTool.InputSchema).Select(text => ($"{tool.Name} argument", text)));
        }

        var listing = await client.CallToolAsync(ToolText.ListToolName, cancellationToken: Token);
        said.Add(("listing", string.Concat(listing.Content.OfType<TextContentBlock>().Select(block => block.Text))));

        Assert.Empty(Offending(said));
    }

    [Fact]
    public void EveryRefusalReleaseAndNotice_HoldsNoneOfTheRetiredWords()
    {
        List<(string Where, string Text)> said = [("keypaste mcp --help", ServerOptions.Usage)];

        said.AddRange(typeof(ToolText)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!)));

        foreach (var method in Enum.GetValues<AuditMethod>())
        {
            said.Add(($"Refusal({method})", ToolText.Refusal(method)));
            said.Add(($"AccessRefusal({method})", ToolText.AccessRefusal(method)));
        }

        said.Add(("Released", ToolText.Released("password", 900)));
        said.Add(("ReleasedOnce", ToolText.ReleasedOnce("password")));
        said.Add(("ReleasedAutomatically", ToolText.ReleasedAutomatically("password", 900)));
        said.Add(("Ran from a person", ToolText.Ran(["DEPLOY_KEY"], AuditMethod.Prompt, 900, 0, 120, 1)));
        said.Add(("Ran under an allowance", ToolText.Ran(["DEPLOY_KEY"], AuditMethod.GrantCache, 600, null, 120, 0)));
        said.Add(("RunNotStarted", ToolText.RunNotStarted("/usr/bin/npm", "no such file")));
        said.Add(("Invalid", ToolText.Invalid("reason", "must not be empty")));

        Assert.Empty(Offending(said));
    }

    private static List<string> Offending(IEnumerable<(string Where, string Text)> said) =>
        [.. said.SelectMany(item => Retired()
            .Matches(Interface().Replace(item.Text, " "))
            .Select(match => $"{item.Where}: '{match.Value}' in {item.Text}"))];

    private static IEnumerable<string> Descriptions(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in schema.EnumerateObject())
        {
            if (property.NameEquals("description") && property.Value.ValueKind == JsonValueKind.String)
            {
                yield return property.Value.GetString()!;
            }
            else
            {
                foreach (var nested in property.Value.ValueKind == JsonValueKind.Array
                    ? property.Value.EnumerateArray().SelectMany(Descriptions)
                    : Descriptions(property.Value))
                {
                    yield return nested;
                }
            }
        }
    }
}
