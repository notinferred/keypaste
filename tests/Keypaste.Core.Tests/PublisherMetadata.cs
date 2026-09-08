using System.Reflection;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What a shipped assembly says about who published it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This reads the built assembly, not <c>Directory.Build.props</c>.</b> The defect F.4b repairs
/// was invisible in the source property anybody would have looked at: <c>Company</c> was never set,
/// the SDK filled <c>CompanyName</c> from <c>Authors</c>, and the shipped binary said something the
/// props file never did. A check that reads the file the value came from cannot catch that.
/// </para>
/// <para>
/// It is compiled into each front end's own test project rather than reflecting over a path,
/// because the assembly a test project references is the one that front end actually builds. The
/// packaged half — the Win32 version resource, the archive, the targets this machine cannot build —
/// belongs to <c>scripts/verify-publisher-metadata.sh</c>, which reads the published bytes.
/// </para>
/// </remarks>
internal static class PublisherMetadata
{
    /// <summary>The project's own identity, recorded in DECISIONS.md D-0097.</summary>
    internal const string Project = "keypaste";

    /// <summary>The project's copyright line.</summary>
    internal const string ProjectCopyright = "Copyright (c) 2026 keypaste";

    /// <summary>Upstream KeePassLib's holder, from THIRD_PARTY_NOTICES.md.</summary>
    internal const string Upstream = "Dominik Reichl";

    /// <summary>Upstream KeePassLib's copyright line.</summary>
    internal const string UpstreamCopyright = "Copyright (c) 2003-2021 Dominik Reichl";

    /// <summary>Asserts that an assembly keypaste publishes is published as keypaste.</summary>
    /// <param name="assembly">The assembly.</param>
    internal static void IsThisProject(Assembly assembly)
    {
        Assert.Equal(Project, Company(assembly));
        Assert.Equal(Project, Product(assembly));
        Assert.Equal(ProjectCopyright, Copyright(assembly));
    }

    /// <summary>
    /// Asserts that the vendored assembly carries upstream's attribution and never keypaste's.
    /// </summary>
    /// <param name="assembly">The assembly.</param>
    internal static void IsUpstreamKeePassLib(Assembly assembly)
    {
        Assert.Equal(Upstream, Company(assembly));
        Assert.Equal(UpstreamCopyright, Copyright(assembly));
        Assert.DoesNotContain(Project, Copyright(assembly) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Company(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;

    private static string? Copyright(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

    private static string? Product(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
}
