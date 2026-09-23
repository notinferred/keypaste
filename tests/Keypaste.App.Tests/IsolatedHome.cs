using System.Runtime.CompilerServices;
using Keypaste.Core.Audit;

namespace Keypaste.App.Tests;

/// <summary>Points every session this process opens at a throwaway home, so vault claims never land in a real one.</summary>
internal static class IsolatedHome
{
    [ModuleInitializer]
    internal static void Apply()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable)))
        {
            Environment.SetEnvironmentVariable(
                KeypasteHome.EnvironmentVariable,
                Directory.CreateTempSubdirectory("keypaste-app-tests-home-").FullName);
        }
    }
}
