using FsCheck;
using FsCheck.Fluent;
using Microsoft.FSharp.Core;

namespace Keypaste.Core.Tests;

/// <summary>Holds a rule over generated cases drawn from one fixed seed, so a run's verdict belongs to its commit.</summary>
internal static class Properties
{
    private static readonly Config _config = Config.QuickThrowOnFailure
        .WithQuietOnSuccess(true)
        .WithReplay(FSharpOption<Replay>.Some(new Replay(new Rnd(20261031, 1), FSharpOption<int>.None)));

    internal static void Hold<T>(Gen<T> cases, Action<T> rule, int count = 200) =>
        Check.One(_config.WithMaxTest(count), Prop.ForAll(cases.ToArbitrary(), rule));
}

/// <summary>Choices a generated case makes as it is built, drawn in order from generated numbers.</summary>
internal sealed class Noise(int[] picks)
{
    internal static Gen<Noise> Any { get; } = Gen.Choose(0, 1 << 20).ArrayOf(64).Select(picks => new Noise(picks));

    private int _at;

    internal T Pick<T>(params T[] options) => options[picks[_at++ % picks.Length] % options.Length];

    internal int Below(int bound) => picks[_at++ % picks.Length] % bound;
}
