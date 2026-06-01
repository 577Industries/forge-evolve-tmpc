// FORGE EVOLVE for TMPC — shared corpus fixture for the validation tests.
//
// Loads the frozen golden corpus (surrogate/corpus/corpus.json) exactly once and shares it
// across the whole test class collection. No real data — synthetic surrogate only.

using ForgeEvolve.Contracts;
using Xunit;

namespace ForgeEvolve.Validation.Tests;

public sealed class CorpusFixture
{
    public LoadedCorpus Corpus { get; }
    public string CorpusPath { get; }
    public ToleranceConfig Tolerance { get; } = new(); // frozen defaults: 1e-9 rel, 1e-12 floor

    public CorpusFixture()
    {
        CorpusPath = CorpusLoader.FindCorpus()
            ?? throw new FileNotFoundException(
                "Could not locate surrogate/corpus/corpus.json by walking up from "
                + AppContext.BaseDirectory);
        Corpus = CorpusLoader.Load(CorpusPath);
    }
}

[CollectionDefinition("corpus")]
public sealed class CorpusCollection : ICollectionFixture<CorpusFixture> { }
