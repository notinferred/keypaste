using Keypaste.Core.Clients;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Only the launcher text npm's cmd-shim writes is read as one, so keypaste starts on Windows what that launcher would,
/// without <c>cmd.exe</c> (D-0436).
/// </summary>
/// <remarks>The fixtures are cmd-shim's output, from its <c>writeShim_</c> at 7.0.0 and 9.0.2, with the CRLF it writes.</remarks>
public sealed class NpmCmdShimTests
{
    internal const string Pnpm = "@SETLOCAL\r\n"
        + "@IF NOT DEFINED NODE_PATH (\r\n"
        + "  @SET \"NODE_PATH=C:\\Users\\me\\AppData\\Local\\pnpm\\global\\5\\node_modules\"\r\n"
        + ") ELSE (\r\n"
        + "  @SET \"NODE_PATH=C:\\Users\\me\\AppData\\Local\\pnpm\\global\\5\\node_modules;%NODE_PATH%\"\r\n"
        + ")\r\n"
        + "@IF EXIST \"%~dp0\\node.exe\" (\r\n"
        + "  \"%~dp0\\node.exe\"  \"%~dp0\\global\\5\\node_modules\\@openai\\codex\\bin\\codex.js\" %*\r\n"
        + ") ELSE (\r\n"
        + "  @SET PATHEXT=%PATHEXT:;.JS;=;%\r\n"
        + "  node  \"%~dp0\\global\\5\\node_modules\\@openai\\codex\\bin\\codex.js\" %*\r\n"
        + ")\r\n";

    internal const string NotNpms = "@echo off\r\nnode \"%~dp0\\node_modules\\@openai\\codex\\bin\\codex.js\" %*\r\n";

    public static TheoryData<string, string> ScriptLaunchers => new()
    {
        { Script(@"node_modules\@openai\codex\bin\codex.js"), @"node_modules\@openai\codex\bin\codex.js" },
        { Script(@"..\@google\gemini-cli\dist\index.js"), @"..\@google\gemini-cli\dist\index.js" },
        { ScriptClearingJsOnTheStartLine(@"node_modules\@openai\codex\bin\codex.js"), @"node_modules\@openai\codex\bin\codex.js" },
        { Script(@"node_modules\@openai\codex\bin\codex.js").ReplaceLineEndings("\n"), @"node_modules\@openai\codex\bin\codex.js" },
    };

    public static TheoryData<string> Others => new()
    {
        NotNpms,
        Pnpm,
        WithShebangArguments(@"node_modules\@openai\codex\bin\codex.js"),
        WithVariables(@"node_modules\@openai\codex\bin\codex.js"),
        Script(@"node_modules\%USERNAME%\bin\codex.js"),
        Script(@"node_modules\a"" & calc & ""b.js"),
        Script(@"node_modules\a!x!\b.js"),
        Script(@"node_modules\a^b.js"),
        Script(@"C:\codex.js"),
        Script(@"\codex.js"),
        Script(string.Empty),
        Script(@"node_modules\@openai\codex\bin\codex.js") + "ECHO more\r\n",
        Direct(@"node_modules\probe\bin\probe.sh"),
        string.Empty,
    };

    [Theory]
    [MemberData(nameof(ScriptLaunchers))]
    public void Npms_script_launcher_names_the_script_node_runs(string text, string expected)
    {
        Assert.True(NpmCmdShim.TryRead(text, out var relative, out var throughNode));

        Assert.Equal(expected, relative);
        Assert.True(throughNode);
    }

    [Fact]
    public void Npms_launcher_for_a_program_names_the_exe_it_starts()
    {
        Assert.True(NpmCmdShim.TryRead(Direct(@"node_modules\@anthropic-ai\claude-code\bin\claude.exe"), out var relative, out var throughNode));

        Assert.Equal(@"node_modules\@anthropic-ai\claude-code\bin\claude.exe", relative);
        Assert.False(throughNode);
    }

    [Theory]
    [MemberData(nameof(Others))]
    public void Any_other_batch_text_is_not_read_as_npms(string text)
    {
        Assert.False(NpmCmdShim.TryRead(text, out var relative, out var throughNode));

        Assert.Empty(relative);
        Assert.False(throughNode);
    }

    /// <summary>What cmd-shim 7.0.0 to 9.0.1 writes for a script whose first line is <c>#!/usr/bin/env node</c>.</summary>
    internal static string Script(string target) => Crlf($"""
        @ECHO off
        GOTO start
        :find_dp0
        SET dp0=%~dp0
        EXIT /b
        :start
        SETLOCAL
        CALL :find_dp0

        IF EXIST "%dp0%\node.exe" (
          SET "_prog=%dp0%\node.exe"
        ) ELSE (
          SET "_prog=node"
          SET PATHEXT=%PATHEXT:;.JS;=;%
        )

        endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & "%_prog%"  "%dp0%\{target}" %*

        """);

    /// <summary>What cmd-shim writes for a file with no shebang, such as an <c>.exe</c>.</summary>
    internal static string Direct(string target) => Crlf($"""
        @ECHO off
        GOTO start
        :find_dp0
        SET dp0=%~dp0
        EXIT /b
        :start
        SETLOCAL
        CALL :find_dp0
        "%dp0%\{target}"   %*

        """);

    private static string ScriptClearingJsOnTheStartLine(string target) => Crlf($"""
        @ECHO off
        GOTO start
        :find_dp0
        SET dp0=%~dp0
        EXIT /b
        :start
        SETLOCAL
        CALL :find_dp0

        IF EXIST "%dp0%\node.exe" (
          SET "_prog=%dp0%\node.exe"
        ) ELSE (
          SET "_prog=node"
        )

        endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & set PATHEXT=%PATHEXT:;.JS;=;% & "%_prog%"  "%dp0%\{target}" %*

        """);

    private static string WithShebangArguments(string target) =>
        Script(target).Replace("\"%_prog%\"  ", "\"%_prog%\" --no-warnings ", StringComparison.Ordinal);

    private static string WithVariables(string target) =>
        Script(target).Replace("CALL :find_dp0\r\n", "CALL :find_dp0\r\n@SET NODE_OPTIONS=--max-old-space-size=4096\r\n", StringComparison.Ordinal);

    private static string Crlf(string text) => text.ReplaceLineEndings("\r\n");
}
