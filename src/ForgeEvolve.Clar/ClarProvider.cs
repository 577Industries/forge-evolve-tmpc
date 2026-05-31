// FORGE EVOLVE for TMPC — IClarProvider implementation.
//
// Stage between Discovery and the Planner/Transformer: lifts source modules into CLAR
// JSON-LD and validates CLAR documents against the FROZEN clar-spec/CLAR.schema.json.
// Implements the frozen ForgeEvolve.Contracts.IClarProvider seam.

using System.Text.Json;
using ForgeEvolve.Clar.Model;
using ForgeEvolve.Contracts;

namespace ForgeEvolve.Clar;

/// <summary>
/// Default <see cref="IClarProvider"/>: a four-layer lifter plus schema validation. Stateless
/// and thread-safe; the schema is loaded once and cached by <see cref="ClarSchema"/>.
/// </summary>
public sealed class ClarProvider : IClarProvider
{
    /// <summary>Lift a module (with its discovery context) into a CLAR JSON-LD document string.</summary>
    public string Lift(ModuleNode module, DiscoveryReport context)
    {
        ClarDocument doc = ClarLifter.Lift(module, context);
        return Serialize(doc);
    }

    /// <summary>
    /// Validate a CLAR document against the frozen schema. Returns errors (empty = valid).
    /// </summary>
    public IReadOnlyList<string> Validate(string clarDocumentJson)
        => ClarSchema.Validate(clarDocumentJson);

    /// <summary>Lift a module into a strongly-typed CLAR document (no serialization).</summary>
    public ClarDocument LiftToModel(ModuleNode module, DiscoveryReport context)
        => ClarLifter.Lift(module, context);

    /// <summary>Serialize a CLAR document to indented JSON-LD using the shared options.</summary>
    public static string Serialize(ClarDocument doc)
        => JsonSerializer.Serialize(doc, ClarSerialization.Options);

    /// <summary>
    /// Lift a module and write the CLAR document to
    /// <c>&lt;resultsRoot&gt;/clar/&lt;module&gt;.clar.jsonld</c>. Returns the absolute path
    /// written. The file name is derived from the module id with path-unsafe characters
    /// replaced. If <paramref name="resultsRoot"/> is null, "results" under the current
    /// directory is used.
    /// </summary>
    public string LiftToFile(ModuleNode module, DiscoveryReport context, string? resultsRoot = null)
    {
        string json = Lift(module, context);
        string root = resultsRoot ?? Path.Combine(Directory.GetCurrentDirectory(), "results");
        string dir = Path.Combine(root, "clar");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{SafeFileStem(module.Id)}.clar.jsonld");
        File.WriteAllText(path, json);
        return Path.GetFullPath(path);
    }

    /// <summary>Make a module id safe to use as a file-name stem.</summary>
    public static string SafeFileStem(string moduleId)
    {
        var sb = new System.Text.StringBuilder(moduleId.Length);
        foreach (char c in moduleId)
            sb.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        string stem = sb.ToString().Trim('_');
        return stem.Length == 0 ? "module" : stem;
    }
}
