using System.Text;
using System.Text.Json.Nodes;
using Keypaste.Core.Clients;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// keypaste's edit of a tool's configuration file changes only its own member, keeps the first original, and leaves alone a
/// file it cannot read safely.
/// </summary>
public sealed class McpConfigFileTests : IDisposable
{
    private const string _servers = "mcpServers";

    private const string _twoSpaces = """
        {
          "mcpServers": {
            "other": { "command": "other" }
          }
        }

        """;

    private static readonly McpConfigOutcome _unchanged = new(McpConfigOutcomeKind.Unchanged, Backup: null, Problem: null);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-config-file-").FullName;

    public static TheoryData<string, string> Insertions => new()
    {
        {
            """
            {
              "mcpServers": {
                "other": {
                  "command": "other"
                }
              }
            }

            """,
            """
            {
              "mcpServers": {
                "keypaste": {
                  "command": "/opt/keypaste/keypaste",
                  "args": [
                    "mcp",
                    "--client-label",
                    "cursor"
                  ]
                },
                "other": {
                  "command": "other"
                }
              }
            }

            """
        },
        {
            """
            {
              "theme": "dark"
            }

            """,
            """
            {
              "mcpServers": {
                "keypaste": {
                  "command": "/opt/keypaste/keypaste",
                  "args": [
                    "mcp",
                    "--client-label",
                    "cursor"
                  ]
                }
              },
              "theme": "dark"
            }

            """
        },
        {
            """
            {
              "mcpServers": {}
            }

            """,
            """
            {
              "mcpServers": {
                "keypaste": {
                  "command": "/opt/keypaste/keypaste",
                  "args": [
                    "mcp",
                    "--client-label",
                    "cursor"
                  ]
                }
              }
            }

            """
        },
        {
            """{"mcpServers":{"other":{"command":"other"}}}""",
            """{"mcpServers":{"keypaste": {"command":"/opt/keypaste/keypaste","args":["mcp","--client-label","cursor"]}, "other":{"command":"other"}}}"""
        },
    };

    public static TheoryData<string, string> Removals => new()
    {
        {
            """
            {
              "mcpServers": {
                "keypaste": {
                  "command": "k"
                },
                "other": {}
              }
            }
            """,
            """
            {
              "mcpServers": {
                "other": {}
              }
            }
            """
        },
        {
            """
            {
              "mcpServers": {
                "other": {},
                "keypaste": {
                  "command": "k"
                }
              }
            }
            """,
            """
            {
              "mcpServers": {
                "other": {}
              }
            }
            """
        },
        {
            """
            {
              "mcpServers": {
                "keypaste": { "command": "k" }
              }
            }
            """,
            """
            {
              "mcpServers": {
              }
            }
            """
        },
        {
            """
            {
              "mcpServers": {
                // keypaste's own
                "keypaste": { "command": "k" }, // a note, with a comma
                "other": {}
              }
            }
            """,
            """
            {
              "mcpServers": {
                // keypaste's own
                // a note, with a comma
                "other": {}
              }
            }
            """
        },
        { """{"mcpServers":{"keypaste":{"command":"k"},"other":{}}}""", """{"mcpServers":{"other":{}}}""" },
        { """{"mcpServers":{"other":{},"keypaste":{"command":"k"}}}""", """{"mcpServers":{"other":{}}}""" },
        { """{"mcpServers":{"keypaste":{"command":"k"} /* not, this */ ,"other":{}}}""", """{"mcpServers":{ /* not, this */ "other":{}}}""" },
        { """{"mcpServers":{"other":{}, /* x, y */ "keypaste":{}}}""", """{"mcpServers":{"other":{} /* x, y */ }}""" },
    };

    public static TheoryData<string> Originals => new()
    {
        _twoSpaces,
        _twoSpaces.ReplaceLineEndings("\r\n"),
        Tabbed(_twoSpaces.Replace("  ", "    ", StringComparison.Ordinal)),
        """{"mcpServers":{"other":{"command":"other"}}}""",
    };

    private string Config => Path.Combine(_directory, "mcp.json");

    private string Backup => Path.Combine(_directory, "mcp.json.keypaste-backup");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_missing_file_gets_only_keypastes_entry_and_no_backup()
    {
        var path = Path.Combine(_directory, "tool", "mcp.json");

        Assert.Equal(new McpConfigOutcome(McpConfigOutcomeKind.Written, Backup: null, Problem: null), Set(path));

        Assert.Equal(
            """
            {
              "mcpServers": {
                "keypaste": {
                  "command": "/opt/keypaste/keypaste",
                  "args": [
                    "mcp",
                    "--client-label",
                    "cursor"
                  ]
                }
              }
            }

            """,
            File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void The_first_original_is_kept_owner_only_and_a_later_edit_leaves_it_alone()
    {
        const string Original = """{ "mcpServers": { "other": { "command": "other" } } }""";
        File.WriteAllText(Config, Original);

        var first = Set(Config);
        var second = Set(Config, label: "renamed");

        Assert.Equal(new McpConfigOutcome(McpConfigOutcomeKind.Written, McpConfigFile.BackupPath(Config), Problem: null), first);
        Assert.Equal(new McpConfigOutcome(McpConfigOutcomeKind.Written, Backup: null, Problem: null), second);
        Assert.Equal(Original, File.ReadAllText(Backup));
        Assert.Contains("renamed", File.ReadAllText(Config), StringComparison.Ordinal);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Backup));
        }
    }

    [Fact]
    public void Replacing_an_entry_keeps_every_other_byte()
    {
        const string Shape = """
            {
              // servers keypaste does not own
              "mcpServers": {
                "other": { "command": "other", "env": { "TOKEN": "abc" } },
                "keypaste": OLD,
                "last": {"command": "last"}
              },
              "theme": "dark"
            }

            """;
        const string Expected = """
            {
              // servers keypaste does not own
              "mcpServers": {
                "other": { "command": "other", "env": { "TOKEN": "abc" } },
                "keypaste": {
                  "command": "/opt/keypaste/keypaste",
                  "args": [
                    "mcp",
                    "--client-label",
                    "cursor"
                  ]
                },
                "last": {"command": "last"}
              },
              "theme": "dark"
            }

            """;
        File.WriteAllBytes(Config, MarkedWithCrLf(Shape.Replace("OLD", """{ "command": "/old/keypaste", "args": [] }""", StringComparison.Ordinal)));

        Assert.Equal(McpConfigOutcomeKind.Written, Set(Config).Kind);

        Assert.Equal(MarkedWithCrLf(Expected), File.ReadAllBytes(Config));
    }

    [Theory]
    [MemberData(nameof(Insertions))]
    public void A_new_entry_goes_first_in_the_servers_object_laid_out_like_the_file(string original, string expected)
    {
        File.WriteAllText(Config, original);

        Assert.Equal(McpConfigOutcomeKind.Written, Set(Config).Kind);

        Assert.Equal(expected, File.ReadAllText(Config));
    }

    [Fact]
    public void A_tab_indented_vs_code_file_gets_a_typed_entry_under_servers_in_its_own_layout()
    {
        File.WriteAllText(Config, Tabbed("""
            {
                // keypaste test
                "servers": {
                    "other": {
                        "type": "stdio",
                        "command": "other"
                    }
                },
                "inputs": []
            }

            """));
        var entry = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = "/opt/keypaste/keypaste",
            ["args"] = new JsonArray("mcp", "--client-label", "vscode"),
        };

        Assert.Equal(McpConfigOutcomeKind.Written, McpConfigFile.Apply(new McpConfigEdit(Config, "servers", entry)).Kind);

        Assert.Equal(
            Tabbed("""
                {
                    // keypaste test
                    "servers": {
                        "keypaste": {
                            "type": "stdio",
                            "command": "/opt/keypaste/keypaste",
                            "args": [
                                "mcp",
                                "--client-label",
                                "vscode"
                            ]
                        },
                        "other": {
                            "type": "stdio",
                            "command": "other"
                        }
                    },
                    "inputs": []
                }

                """),
            File.ReadAllText(Config));
    }

    [Theory]
    [MemberData(nameof(Removals))]
    public void Removing_deletes_only_the_member_and_one_comma(string original, string expected)
    {
        File.WriteAllText(Config, original);

        Assert.Equal(McpConfigOutcomeKind.Written, Remove(Config).Kind);

        Assert.Equal(expected, File.ReadAllText(Config));
        Assert.Equal(original, File.ReadAllText(Backup));
    }

    [Theory]
    [MemberData(nameof(Originals))]
    public void Connecting_then_removing_gives_back_the_original_bytes(string original)
    {
        File.WriteAllText(Config, original);

        Assert.Equal(McpConfigOutcomeKind.Written, Set(Config).Kind);
        Assert.Equal(McpConfigOutcomeKind.Written, Remove(Config).Kind);

        Assert.Equal(original, File.ReadAllText(Config));
        Assert.Equal(original, File.ReadAllText(Backup));
    }

    [Fact]
    public void Removing_an_entry_that_is_not_there_writes_nothing()
    {
        const string Original = """{ "mcpServers": { "other": {} } }""";
        File.WriteAllText(Config, Original);
        var missing = Path.Combine(_directory, "missing", "mcp.json");

        Assert.Equal(_unchanged, Remove(Config));
        Assert.Equal(_unchanged, Remove(missing));

        Assert.Equal(Original, File.ReadAllText(Config));
        Assert.Equal([Config], Directory.GetFiles(_directory));
        Assert.False(Directory.Exists(Path.GetDirectoryName(missing)));
    }

    [Fact]
    public void Setting_the_entry_a_file_already_holds_writes_nothing()
    {
        const string Holding = """{ "mcpServers": { "keypaste": { "command": "/opt/keypaste/keypaste", "args": [ "mcp", "--client-label", "cursor" ] } } }""";
        File.WriteAllText(Config, Holding);

        Assert.Equal(_unchanged, Set(Config));

        Assert.Equal(Holding, File.ReadAllText(Config));
        Assert.Equal([Config], Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("""{ "mcpServers": }""", "is not valid JSON at line 1, byte ")]
    [InlineData("[]", "is not a JSON object")]
    [InlineData("\"keypaste\"", "is not a JSON object")]
    [InlineData("""{ "mcpServers": [] }""", "has a \"mcpServers\" that is not an object")]
    [InlineData("""{ "mcpServers": {}, "mcpServers": {} }""", "holds \"mcpServers\" twice")]
    [InlineData("""{ "mcpServers": { "keypaste": {}, "keypaste": {} } }""", "holds \"keypaste\" twice in \"mcpServers\"")]
    [InlineData("""{ "mcpServers": {}, "theme": 1, "theme": 2 }""", "holds a key twice")]
    public void A_file_keypaste_cannot_read_safely_is_left_alone(string text, string problem)
    {
        File.WriteAllText(Config, text);

        var outcome = Set(Config);

        Assert.Equal(McpConfigOutcomeKind.Refused, outcome.Kind);
        Assert.StartsWith($"{Config} {problem}", outcome.Problem, StringComparison.Ordinal);
        Assert.EndsWith("; nothing was written", outcome.Problem, StringComparison.Ordinal);
        Assert.Null(outcome.Backup);
        Assert.Equal(text, File.ReadAllText(Config));
        Assert.Equal([Config], Directory.GetFiles(_directory));
    }

    [Fact]
    public void A_file_changed_before_the_commit_is_left_as_the_other_writer_left_it()
    {
        const string Original = """{ "mcpServers": {} }""";
        const string Theirs = """{ "mcpServers": { "theirs": {} } }""";
        File.WriteAllText(Config, Original);

        var outcome = McpConfigFile.Apply(
            new McpConfigEdit(Config, _servers, Entry()),
            beforeCommit: () => File.WriteAllText(Config, Theirs));

        Assert.Equal(McpConfigOutcomeKind.Refused, outcome.Kind);
        Assert.Equal($"{Config} changed while keypaste was editing it; nothing was written", outcome.Problem);
        Assert.Equal(Theirs, File.ReadAllText(Config));
        Assert.Equal(Original, File.ReadAllText(Backup));
        Assert.Equal([Config, Backup], Directory.GetFiles(_directory).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void A_linked_file_is_edited_where_it_lives_and_its_original_is_kept_beside_the_link()
    {
        const string Original = """{ "mcpServers": {} }""";
        var dotfiles = Directory.CreateDirectory(Path.Combine(_directory, "dotfiles")).FullName;
        var real = Path.Combine(dotfiles, "mcp.json");
        File.WriteAllText(real, Original);
        Link(Config, real);

        var outcome = Set(Config);

        Assert.Equal(new McpConfigOutcome(McpConfigOutcomeKind.Written, McpConfigFile.BackupPath(Config), Problem: null), outcome);
        Assert.NotNull(new FileInfo(Config).LinkTarget);
        Assert.Contains("/opt/keypaste/keypaste", File.ReadAllText(real), StringComparison.Ordinal);
        Assert.Equal(Original, File.ReadAllText(Backup));
        Assert.Equal([real], Directory.GetFiles(dotfiles));
    }

    [Fact]
    public void An_edited_file_keeps_its_mode_and_a_new_one_is_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no Unix file mode; the file inherits its directory's ACL.");
            return;
        }

        const UnixFileMode Shared = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        var fresh = Path.Combine(_directory, "fresh.json");
        File.WriteAllText(Config, """{ "mcpServers": {} }""");
        File.SetUnixFileMode(Config, Shared);

        Assert.Equal(McpConfigOutcomeKind.Written, Set(Config).Kind);
        Assert.Equal(McpConfigOutcomeKind.Written, Set(fresh).Kind);

        Assert.Equal(Shared, File.GetUnixFileMode(Config));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fresh));
    }

    [Theory]
    [InlineData("""{ "mcpServers": { "keypaste": {} } }""", McpEntryPresence.Present)]
    [InlineData("﻿{ \"mcpServers\": { \"keypaste\": {} } }", McpEntryPresence.Present)]
    [InlineData("{\n  // a note\n  \"mcpServers\": { \"keypaste\": {}, },\n}", McpEntryPresence.Present)]
    [InlineData("""{ "mcpServers": { "other": {} } }""", McpEntryPresence.Absent)]
    [InlineData("""{ "theme": "dark" }""", McpEntryPresence.Absent)]
    [InlineData(" \n", McpEntryPresence.Absent)]
    [InlineData("""{ "mcpServers": [] }""", McpEntryPresence.Unknown)]
    [InlineData("[]", McpEntryPresence.Unknown)]
    [InlineData("""{ "mcpServers": {}, "mcpServers": {} }""", McpEntryPresence.Unknown)]
    [InlineData("""{ "mcpServers": { "keypaste": {}, "keypaste": {} } }""", McpEntryPresence.Unknown)]
    [InlineData("""{ "mcpServers": { "keypaste": {} }, "theme": 1, "theme": 2 }""", McpEntryPresence.Unknown)]
    [InlineData("""{ "mcpServers": SECRET }""", McpEntryPresence.Unknown)]
    public void Locating_the_entry_allows_comments_and_says_unknown_without_quoting_the_file(string text, McpEntryPresence expected)
    {
        var presence = McpConfigFile.Locate(Encoding.UTF8.GetBytes(text), _servers, out var problem);

        Assert.Equal(expected, presence);
        Assert.Equal(expected == McpEntryPresence.Unknown, problem is not null);
        Assert.DoesNotContain("SECRET", problem ?? string.Empty, StringComparison.Ordinal);
    }

    private static JsonObject Entry(string label = "cursor") => new()
    {
        ["command"] = "/opt/keypaste/keypaste",
        ["args"] = new JsonArray("mcp", "--client-label", label),
    };

    private static McpConfigOutcome Set(string path, string label = "cursor") =>
        McpConfigFile.Apply(new McpConfigEdit(path, _servers, Entry(label)));

    private static McpConfigOutcome Remove(string path) => McpConfigFile.Apply(new McpConfigEdit(path, _servers, Entry: null));

    private static string Tabbed(string lines) => lines.Replace("    ", "\t", StringComparison.Ordinal);

    private static byte[] MarkedWithCrLf(string lines) => [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(lines.ReplaceLineEndings("\r\n"))];

    private static void Link(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create symbolic links: {ex.Message}");
        }
    }
}
