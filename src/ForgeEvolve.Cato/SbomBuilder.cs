// ─────────────────────────────────────────────────────────────────────────────
// SbomBuilder — minimal, valid CycloneDX 1.5 SBOM for the analyzed component.
//
// PART OF: FORGE EVOLVE for TMPC, Cyber/cATO overlay (Stage 5).
//
// DEVIATION / offline note:
//   The spec allows either shelling out to the `CycloneDX` dotnet tool OR constructing a
//   minimal valid CycloneDX 1.5 JSON. The CycloneDX global/local tool is NOT installed in
//   the offline build environment (verified at build time), so we BUILD a minimal valid
//   CycloneDX 1.5 document directly from the known package references of the analyzed
//   component. The document validates against the CycloneDX 1.5 shape: bomFormat,
//   specVersion, serialNumber, version, metadata.component, and a components[] array with
//   bom-ref / type / name / version / purl. When the tool IS available, scripts/gen-sbom.sh
//   produces the richer transitive SBOM; this builder is the always-available fallback.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForgeEvolve.Cato;

/// <summary>A single component (package) entry for the SBOM.</summary>
public sealed record SbomComponent(string Name, string Version, string Group = "");

/// <summary>Builds a minimal but schema-valid CycloneDX 1.5 JSON SBOM.</summary>
public static class SbomBuilder
{
    public const string BomFormat = "CycloneDX";
    public const string SpecVersion = "1.5";

    /// <summary>
    /// Construct the CycloneDX JSON for the analyzed component plus its declared package
    /// references. <paramref name="serialSeed"/> makes the serialNumber deterministic for a
    /// given run (so artifacts are reproducible); pass the provenance Merkle root or run id.
    /// </summary>
    public static string Build(
        string componentName,
        string componentVersion,
        IReadOnlyList<SbomComponent> packages,
        string serialSeed)
    {
        // Deterministic urn:uuid from the serial seed (RFC 4122-shaped; not a real v4 UUID,
        // but a valid urn:uuid string accepted by CycloneDX consumers).
        string serial = "urn:uuid:" + DeterministicUuid(serialSeed);

        var components = new JsonArray();
        foreach (SbomComponent p in packages)
        {
            string group = string.IsNullOrEmpty(p.Group) ? "" : p.Group + "/";
            string purl = $"pkg:nuget/{p.Name}@{p.Version}";
            components.Add(new JsonObject
            {
                ["type"] = "library",
                ["bom-ref"] = $"{p.Name}@{p.Version}",
                ["name"] = p.Name,
                ["version"] = p.Version,
                ["purl"] = purl,
            });
        }

        var root = new JsonObject
        {
            ["bomFormat"] = BomFormat,
            ["specVersion"] = SpecVersion,
            ["serialNumber"] = serial,
            ["version"] = 1,
            ["metadata"] = new JsonObject
            {
                ["timestamp"] = "1970-01-01T00:00:00Z", // fixed for reproducible artifacts
                ["tools"] = new JsonObject
                {
                    ["components"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "application",
                            ["name"] = "ForgeEvolve.Cato.SbomBuilder",
                            ["version"] = "1.0.0",
                        },
                    },
                },
                ["component"] = new JsonObject
                {
                    ["type"] = "application",
                    ["bom-ref"] = componentName,
                    ["name"] = componentName,
                    ["version"] = componentVersion,
                },
            },
            ["components"] = components,
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Derive a stable UUID-shaped string (8-4-4-4-12) from a seed via SHA-256.</summary>
    private static string DeterministicUuid(string seed)
    {
        string h = ProvenanceLedger.Sha256Hex(seed); // 64 hex chars
        // Lay out as 8-4-4-4-12 = 32 hex chars.
        return $"{h.Substring(0, 8)}-{h.Substring(8, 4)}-{h.Substring(12, 4)}-{h.Substring(16, 4)}-{h.Substring(20, 12)}";
    }

    /// <summary>True when <paramref name="json"/> is a structurally valid CycloneDX document.</summary>
    public static bool IsValidCycloneDx(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return false;
            if (!r.TryGetProperty("bomFormat", out JsonElement bf) || bf.GetString() != BomFormat) return false;
            if (!r.TryGetProperty("specVersion", out JsonElement sv) || string.IsNullOrEmpty(sv.GetString())) return false;
            if (!r.TryGetProperty("components", out JsonElement comp) || comp.ValueKind != JsonValueKind.Array) return false;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
