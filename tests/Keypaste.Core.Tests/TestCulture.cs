using System.Globalization;
using System.Runtime.CompilerServices;

namespace Keypaste.Core.Tests;

/// <summary>Runs this assembly under the culture <c>KEYPASTE_TEST_CULTURE</c> names, with ICU on through <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0</c>, as <c>verify.sh backend</c> does.</summary>
internal static class TestCulture
{
    internal const string Variable = "KEYPASTE_TEST_CULTURE";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Apply()
    {
        if (Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } name)
        {
            var culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
