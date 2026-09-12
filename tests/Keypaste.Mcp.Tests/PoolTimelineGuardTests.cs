using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// An assembly that was asked for a timeline writes one, or this run is not a measurement.
/// </summary>
/// <remarks>
/// F.9's producer correlation is read by merging what two test assemblies wrote while they ran
/// concurrently. The timeline is behind an environment variable so an ordinary run carries none of
/// it — which is exactly how a probe spends two hundred and forty suite runs, reports a full set of
/// failure counts, and comes back with an empty artifact that cannot answer the question the counts
/// were gathered for. This makes that a red test in the assembly it happened to, rather than a
/// discovery made after the runner time is gone.
/// </remarks>
public sealed class PoolTimelineGuardTests
{
    [Fact]
    public void WhenAProbeAsksForATimeline_ThisAssemblyIsOnIt() => PoolTimeline.EnsureWritten();
}
