using System.Runtime.CompilerServices;
using Keypaste.Core.Internal;

namespace Keypaste.Core.Tests;

/// <summary>Has every vault this test assembly creates take a token Argon2 cost; a test about the production cost opts back in (D-0469).</summary>
internal static class CheapKeyDerivation
{
    [ModuleInitializer]
    internal static void Apply() => TestKeyDerivation.Cheap = true;
}
