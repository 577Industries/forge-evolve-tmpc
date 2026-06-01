// ─────────────────────────────────────────────────────────────────────────────
// SpectralPartitioner — Fiedler-vector spectral bipartition of an undirected weighted graph.
//
// PART OF: FORGE EVOLVE for TMPC, Migration Planner (Stage 2, workstream WS-C).
//
// Used to PARTITION the large coupling cluster (the god class + its methods + its data/rule
// affinities) into candidate microservice boundaries.
//
// Method (textbook spectral graph partitioning, no external dependency):
//   1. Build the weighted adjacency W (symmetric) and the degree matrix D.
//   2. The graph Laplacian L = D - W is symmetric PSD; its smallest eigenvalue is 0 with the
//      all-ones eigenvector (1). The SECOND-smallest eigenvalue (the "algebraic connectivity") has
//      the Fiedler eigenvector, whose sign pattern gives the minimum-normalized-cut bipartition.
//   3. We want the smallest non-trivial eigenvector of L. Power iteration finds the LARGEST
//      eigenvalue, so we iterate on the DEFLATED, SHIFTED matrix  B = sigma*I - L  (sigma >= lambda_max)
//      while DEFLATING the all-ones vector out of every iterate (Hotelling deflation). The dominant
//      eigenvector of B, orthogonal to 1, is exactly the Fiedler vector of L.
//   4. Bipartition by the sign of each component relative to the WEIGHTED MEDIAN (balances the cut).
//
// Determinism: a fixed start vector and fixed iteration budget make the result reproducible.
// ─────────────────────────────────────────────────────────────────────────────

namespace ForgeEvolve.Planner;

/// <summary>An undirected weighted similarity graph over a fixed, ordered node list.</summary>
internal sealed class WeightedGraph
{
    public IReadOnlyList<string> Nodes { get; }
    private readonly Dictionary<string, int> _idx;
    private readonly double[,] _w;

    public WeightedGraph(IReadOnlyList<string> nodes)
    {
        Nodes = nodes;
        _idx = new Dictionary<string, int>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++) _idx[nodes[i]] = i;
        _w = new double[nodes.Count, nodes.Count];
    }

    public int Count => Nodes.Count;
    public double[,] Weights => _w;

    /// <summary>Add (or accumulate) symmetric affinity between two nodes.</summary>
    public void AddAffinity(string a, string b, double weight)
    {
        if (a == b) return;
        if (!_idx.TryGetValue(a, out int i) || !_idx.TryGetValue(b, out int j)) return;
        _w[i, j] += weight;
        _w[j, i] += weight;
    }
}

internal static class SpectralPartitioner
{
    public sealed record Bipartition(IReadOnlyList<string> Left, IReadOnlyList<string> Right, double[] Fiedler);

    /// <summary>
    /// Compute the Fiedler vector of the graph Laplacian via deflated/shifted power iteration.
    /// Returns one value per node (in <c>graph.Nodes</c> order).
    /// </summary>
    public static double[] FiedlerVector(WeightedGraph graph, int maxIterations = 1000, double tol = 1e-10)
    {
        int n = graph.Count;
        if (n == 0) return Array.Empty<double>();
        if (n == 1) return new[] { 0.0 };

        double[,] w = graph.Weights;

        // Degree vector and Laplacian L = D - W (built implicitly via matrix-vector products).
        var deg = new double[n];
        for (int i = 0; i < n; i++)
        {
            double d = 0;
            for (int j = 0; j < n; j++) d += w[i, j];
            deg[i] = d;
        }

        // Gershgorin upper bound on lambda_max(L): max_i (deg_i + sum_j|W_ij|) = max_i 2*deg_i.
        double sigma = 0.0;
        for (int i = 0; i < n; i++) sigma = Math.Max(sigma, 2.0 * deg[i]);
        if (sigma <= 0) return new double[n]; // empty graph -> degenerate, no structure

        // Apply B = sigma*I - L = sigma*I - (D - W) = (sigma*I - D) + W  to a vector x.
        double[] ApplyB(double[] x)
        {
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double acc = (sigma - deg[i]) * x[i];
                for (int j = 0; j < n; j++) acc += w[i, j] * x[j];
                y[i] = acc;
            }
            return y;
        }

        // Deterministic, non-degenerate start vector orthogonalized against the all-ones vector.
        var x0 = new double[n];
        for (int i = 0; i < n; i++) x0[i] = Math.Sin(i + 1.0); // deterministic, not constant
        DeflateOnes(x0);
        Normalize(x0);

        var x = x0;
        for (int it = 0; it < maxIterations; it++)
        {
            var y = ApplyB(x);
            DeflateOnes(y);          // keep the iterate orthogonal to the trivial eigenvector 1
            double norm = Norm(y);
            if (norm < 1e-300) break; // collapsed to the null space; structure is flat
            for (int i = 0; i < n; i++) y[i] /= norm;

            double diff = 0;
            for (int i = 0; i < n; i++) diff += Math.Abs(Math.Abs(y[i]) - Math.Abs(x[i]));
            x = y;
            if (diff < tol) break;
        }
        return x;
    }

    /// <summary>
    /// Spectral bipartition by the SIGN of the Fiedler vector (threshold at 0 — the standard
    /// minimum-normalized-cut rule). Because the Fiedler vector is centered (orthogonal to the
    /// all-ones vector), the sign split follows the true community structure and recovers communities
    /// of ANY size (it does not force a balanced cut). If the sign split is degenerate (all components
    /// share a sign, e.g. a structureless graph), it falls back to a median split so the partition is
    /// always non-trivial when there is more than one node.
    /// </summary>
    public static Bipartition Bisect(WeightedGraph graph)
    {
        var fiedler = FiedlerVector(graph);
        int n = graph.Count;
        if (n <= 1)
            return new Bipartition(graph.Nodes.ToList(), Array.Empty<string>(), fiedler);

        var left = new List<string>();
        var right = new List<string>();
        // Threshold at 0: the centered Fiedler vector's sign is the community indicator.
        for (int i = 0; i < n; i++)
            (fiedler[i] < 0 ? left : right).Add(graph.Nodes[i]);

        // Fallback for a degenerate sign split (no sign change => no separating structure at 0):
        // split at the median so we still return two non-empty sides.
        if (left.Count == 0 || right.Count == 0)
        {
            left.Clear(); right.Clear();
            var order = Enumerable.Range(0, n).OrderBy(i => fiedler[i]).ThenBy(i => i).ToArray();
            for (int k = 0; k < n; k++)
                (k < n / 2 ? left : right).Add(graph.Nodes[order[k]]);
        }
        return new Bipartition(left, right, fiedler);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static void DeflateOnes(double[] v)
    {
        // Remove the component along the all-ones vector: v <- v - (mean) * 1.
        double mean = 0;
        for (int i = 0; i < v.Length; i++) mean += v[i];
        mean /= v.Length;
        for (int i = 0; i < v.Length; i++) v[i] -= mean;
    }

    private static double Norm(double[] v)
    {
        double s = 0;
        for (int i = 0; i < v.Length; i++) s += v[i] * v[i];
        return Math.Sqrt(s);
    }

    private static void Normalize(double[] v)
    {
        double nrm = Norm(v);
        if (nrm < 1e-300) return;
        for (int i = 0; i < v.Length; i++) v[i] /= nrm;
    }
}
