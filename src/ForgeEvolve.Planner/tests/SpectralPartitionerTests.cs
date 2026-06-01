// ─────────────────────────────────────────────────────────────────────────────
// SpectralPartitionerTests — synthetic-graph unit tests for the Fiedler bipartition.
//
// PART OF: FORGE EVOLVE for TMPC, Migration Planner tests (workstream WS-C).
//
// Validates the spectral partitioner independently of the surrogate on a KNOWN two-community graph
// (two cliques joined by a single weak bridge). The Fiedler-vector bipartition must recover the two
// planted communities exactly. Also checks the trivial/degenerate cases.
// ─────────────────────────────────────────────────────────────────────────────

using Xunit;

namespace ForgeEvolve.Planner.Tests;

public sealed class SpectralPartitionerTests
{
    [Fact]
    public void Bisect_RecoversTwoPlantedCommunities_OnABarbellGraph()
    {
        // Community A = {a0,a1,a2,a3} fully connected; Community B = {b0,b1,b2,b3} fully connected;
        // a single weak bridge a0--b0 joins them. The min-cut bipartition is exactly A | B.
        var nodes = new[] { "a0", "a1", "a2", "a3", "b0", "b1", "b2", "b3" };
        var g = new WeightedGraph(nodes);

        var a = new[] { "a0", "a1", "a2", "a3" };
        var b = new[] { "b0", "b1", "b2", "b3" };
        Clique(g, a, weight: 5.0);
        Clique(g, b, weight: 5.0);
        g.AddAffinity("a0", "b0", 0.5); // weak bridge

        var part = SpectralPartitioner.Bisect(g);

        var left = part.Left.ToHashSet();
        var right = part.Right.ToHashSet();

        // Each planted community must be entirely on one side (orientation-independent).
        bool aTogether = a.All(left.Contains) || a.All(right.Contains);
        bool bTogether = b.All(left.Contains) || b.All(right.Contains);
        bool separated = a.All(left.Contains) ? b.All(right.Contains) : b.All(left.Contains);

        Assert.True(aTogether, "Community A was split across the bipartition.");
        Assert.True(bTogether, "Community B was split across the bipartition.");
        Assert.True(separated, "The two communities landed on the same side.");
        Assert.Equal(4, part.Left.Count);
        Assert.Equal(4, part.Right.Count);
    }

    [Fact]
    public void FiedlerVector_HasOppositeSigns_AcrossTheCut()
    {
        // On the barbell graph, the Fiedler vector's sign separates the two communities.
        var nodes = new[] { "a0", "a1", "a2", "b0", "b1", "b2" };
        var g = new WeightedGraph(nodes);
        Clique(g, new[] { "a0", "a1", "a2" }, 4.0);
        Clique(g, new[] { "b0", "b1", "b2" }, 4.0);
        g.AddAffinity("a0", "b0", 0.5);

        var f = SpectralPartitioner.FiedlerVector(g);
        // Index map.
        var idx = nodes.Select((n, i) => (n, i)).ToDictionary(t => t.n, t => t.i);
        double aMean = (f[idx["a0"]] + f[idx["a1"]] + f[idx["a2"]]) / 3.0;
        double bMean = (f[idx["b0"]] + f[idx["b1"]] + f[idx["b2"]]) / 3.0;

        // The two community means must sit on opposite sides of 0 (the Fiedler vector is centered).
        Assert.True(Math.Sign(aMean) != Math.Sign(bMean) && aMean != 0 && bMean != 0,
            $"Expected opposite-sign community means, got aMean={aMean}, bMean={bMean}.");
    }

    [Fact]
    public void Bisect_OnAsymmetricCommunities_KeepsTheDenseClusterIntact()
    {
        // Community sizes 3 and 5 joined by one bridge: the dense clusters must stay intact.
        var nodes = new[] { "x0", "x1", "x2", "y0", "y1", "y2", "y3", "y4" };
        var g = new WeightedGraph(nodes);
        Clique(g, new[] { "x0", "x1", "x2" }, 6.0);
        Clique(g, new[] { "y0", "y1", "y2", "y3", "y4" }, 6.0);
        g.AddAffinity("x0", "y0", 0.4);

        var part = SpectralPartitioner.Bisect(g);
        var left = part.Left.ToHashSet();
        var right = part.Right.ToHashSet();

        var x = new[] { "x0", "x1", "x2" };
        var y = new[] { "y0", "y1", "y2", "y3", "y4" };
        bool xTogether = x.All(left.Contains) || x.All(right.Contains);
        bool yTogether = y.All(left.Contains) || y.All(right.Contains);
        Assert.True(xTogether, "Small community X was split.");
        Assert.True(yTogether, "Large community Y was split.");
    }

    [Fact]
    public void Bisect_SingleNode_IsDegenerateButSafe()
    {
        var g = new WeightedGraph(new[] { "solo" });
        var part = SpectralPartitioner.Bisect(g);
        Assert.Single(part.Left);
        Assert.Empty(part.Right);
    }

    private static void Clique(WeightedGraph g, string[] nodes, double weight)
    {
        for (int i = 0; i < nodes.Length; i++)
            for (int j = i + 1; j < nodes.Length; j++)
                g.AddAffinity(nodes[i], nodes[j], weight);
    }
}
