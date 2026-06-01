// ModernCheck — THE BEHAVIORAL-EQUIVALENCE PROOF.
//
// Replays the frozen golden corpus through the modern MissionService.ProcessMission and asserts
// its output EQUALS the stored Python `legacyOutput` answer key (discrete fields exact incl.
// messages; continuous distance fields within 1e-9 relative). Prints "MODERN-CHECK PASS: N/N" on
// success (exit 0); otherwise lists mismatches and exits 1.
//
// This is the zero-silent-regression gate: the modern, cleanly re-architected component must
// reproduce the legacy D1/D2/D3 quirks EXACTLY. If this fails, the refactor changed behavior.

using System.Globalization;
using System.Text.Json;
using ForgeEvolve.ModernMds.Services;

namespace ForgeEvolve.ModernMds.Tools;

internal static class Program
{
    private const double RelTol = 1e-9;

    private static int Main(string[] args)
    {
        string? corpusPath = args.Length > 0 ? args[0] : FindCorpus();
        if (corpusPath is null || !File.Exists(corpusPath))
        {
            Console.Error.WriteLine(
                "MODERN-CHECK ERROR: could not locate corpus.json (pass its path as arg 1).");
            return 2;
        }

        using JsonDocument corpus = JsonDocument.Parse(File.ReadAllText(corpusPath));
        JsonElement arr = corpus.RootElement;
        if (arr.ValueKind != JsonValueKind.Array)
        {
            Console.Error.WriteLine("MODERN-CHECK ERROR: corpus root is not an array.");
            return 2;
        }

        // Behavior-preserving service: publish disabled by default (output-neutral).
        MissionService service = MissionService.CreateDefault();

        int n = 0;
        int passed = 0;
        var mismatches = new List<string>();

        foreach (JsonElement vec in arr.EnumerateArray())
        {
            n++;
            string id = vec.GetProperty("id").GetString() ?? "<no-id>";
            string inputJson = vec.GetProperty("input").GetRawText();
            JsonElement expected = vec.GetProperty("legacyOutput");

            string actualJson = service.ProcessMission(inputJson);
            using JsonDocument actualDoc = JsonDocument.Parse(actualJson);

            string? err = Compare(expected, actualDoc.RootElement);
            if (err is null)
            {
                passed++;
            }
            else if (mismatches.Count < 25)
            {
                mismatches.Add(id + ": " + err);
            }
        }

        if (passed == n)
        {
            Console.WriteLine("MODERN-CHECK PASS: " + passed + "/" + n);
            return 0;
        }

        Console.WriteLine("MODERN-CHECK FAIL: " + passed + "/" + n + " (" + (n - passed) + " mismatches)");
        foreach (string m in mismatches) Console.WriteLine("  " + m);
        if (n - passed > mismatches.Count)
            Console.WriteLine("  ... and " + (n - passed - mismatches.Count) + " more.");
        return 1;
    }

    // Returns null on match, otherwise a human-readable description of the first diff.
    private static string? Compare(JsonElement expected, JsonElement actual)
    {
        string? discrete = CompareDiscrete(expected, actual);
        if (discrete is not null) return discrete;

        string? msgs = CompareMessages(expected, actual);
        if (msgs is not null) return msgs;

        return CompareContinuous(expected, actual);
    }

    private static string? CompareDiscrete(JsonElement expected, JsonElement actual)
    {
        string? emid = expected.GetProperty("missionId").GetString();
        string? amid = actual.GetProperty("missionId").GetString();
        if (emid != amid) return "missionId " + emid + " != " + amid;

        if (expected.GetProperty("routeValid").GetBoolean() != actual.GetProperty("routeValid").GetBoolean())
            return "routeValid differs";
        if (expected.GetProperty("totFeasible").GetBoolean() != actual.GetProperty("totFeasible").GetBoolean())
            return "totFeasible differs";
        if (expected.GetProperty("taskingGoNoGo").GetBoolean() != actual.GetProperty("taskingGoNoGo").GetBoolean())
            return "taskingGoNoGo differs";

        long eTot = expected.GetProperty("estimatedTotEpochSec").GetInt64();
        long aTot = actual.GetProperty("estimatedTotEpochSec").GetInt64();
        if (eTot != aTot) return "estimatedTotEpochSec " + eTot + " != " + aTot;

        return null;
    }

    private static string? CompareMessages(JsonElement expected, JsonElement actual)
    {
        JsonElement em = expected.GetProperty("messages");
        JsonElement am = actual.GetProperty("messages");
        if (em.GetArrayLength() != am.GetArrayLength())
            return "messages length " + em.GetArrayLength() + " != " + am.GetArrayLength();

        JsonElement.ArrayEnumerator ee = em.EnumerateArray();
        JsonElement.ArrayEnumerator ae = am.EnumerateArray();
        while (ee.MoveNext() && ae.MoveNext())
        {
            if (ee.Current.GetString() != ae.Current.GetString())
                return "message '" + ee.Current.GetString() + "' != '" + ae.Current.GetString() + "'";
        }
        return null;
    }

    private static string? CompareContinuous(JsonElement expected, JsonElement actual)
    {
        double eTotal = expected.GetProperty("totalDistanceNm").GetDouble();
        double aTotal = actual.GetProperty("totalDistanceNm").GetDouble();
        if (RelErr(eTotal, aTotal) > RelTol)
            return "totalDistanceNm rel-err " + RelErr(eTotal, aTotal).ToString("E3", CultureInfo.InvariantCulture)
                + " (" + eTotal + " vs " + aTotal + ")";

        JsonElement el = expected.GetProperty("legDistancesNm");
        JsonElement al = actual.GetProperty("legDistancesNm");
        if (el.GetArrayLength() != al.GetArrayLength())
            return "legDistancesNm length " + el.GetArrayLength() + " != " + al.GetArrayLength();

        JsonElement.ArrayEnumerator ee = el.EnumerateArray();
        JsonElement.ArrayEnumerator ae = al.EnumerateArray();
        int i = 0;
        while (ee.MoveNext() && ae.MoveNext())
        {
            double ev = ee.Current.GetDouble();
            double av = ae.Current.GetDouble();
            if (RelErr(ev, av) > RelTol)
                return "legDistancesNm[" + i + "] rel-err "
                    + RelErr(ev, av).ToString("E3", CultureInfo.InvariantCulture)
                    + " (" + ev + " vs " + av + ")";
            i++;
        }
        return null;
    }

    private static double RelErr(double a, double b)
    {
        double denom = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-12);
        return Math.Abs(a - b) / denom;
    }

    // Walk up from the executable to find surrogate/corpus/corpus.json.
    private static string? FindCorpus()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 14 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "surrogate", "corpus", "corpus.json");
            if (File.Exists(candidate)) return candidate;
            string candidate2 = Path.Combine(dir, "corpus", "corpus.json");
            if (File.Exists(candidate2)) return candidate2;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
