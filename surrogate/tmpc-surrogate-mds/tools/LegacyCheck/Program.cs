// LegacyCheck — replay the frozen golden corpus through the C# legacy MissionProcessor
// and assert its output equals the stored Python `legacyOutput` answer key.
//
// PART OF: FORGE EVOLVE for TMPC, synthetic unclassified surrogate (Phase 1).
//
// Discrete fields exact; continuous distance fields within 1e-9 relative.
// Prints "LEGACY-CHECK PASS: N/N" on success; otherwise lists mismatches and exits 1.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Tmpc.Surrogate.Legacy;

namespace Tmpc.Surrogate.Tools
{
    internal static class Program
    {
        private const double RelTol = 1e-9;

        private static int Main(string[] args)
        {
            string corpusPath = args.Length > 0 ? args[0] : FindCorpus();
            if (corpusPath == null || !File.Exists(corpusPath))
            {
                Console.Error.WriteLine("LEGACY-CHECK ERROR: could not locate corpus.json "
                    + "(pass its path as arg 1).");
                return 2;
            }

            string text = File.ReadAllText(corpusPath);
            JsonDocument corpus = JsonDocument.Parse(text);
            JsonElement arr = corpus.RootElement;
            if (arr.ValueKind != JsonValueKind.Array)
            {
                Console.Error.WriteLine("LEGACY-CHECK ERROR: corpus root is not an array.");
                return 2;
            }

            int n = 0;
            int passed = 0;
            var mismatches = new List<string>();

            foreach (JsonElement vec in arr.EnumerateArray())
            {
                n++;
                string id = vec.GetProperty("id").GetString();
                JsonElement input = vec.GetProperty("input");
                JsonElement expected = vec.GetProperty("legacyOutput");

                string inputJson = input.GetRawText();
                string actualJson = MissionProcessor.ProcessMission(inputJson);

                JsonDocument actualDoc = JsonDocument.Parse(actualJson);
                JsonElement actual = actualDoc.RootElement;

                string err = Compare(expected, actual);
                if (err == null)
                {
                    passed++;
                }
                else
                {
                    if (mismatches.Count < 25)
                        mismatches.Add(id + ": " + err);
                }
                actualDoc.Dispose();
            }

            if (passed == n)
            {
                Console.WriteLine("LEGACY-CHECK PASS: " + passed + "/" + n);
                return 0;
            }

            Console.WriteLine("LEGACY-CHECK FAIL: " + passed + "/" + n
                + " (" + (n - passed) + " mismatches)");
            foreach (string m in mismatches) Console.WriteLine("  " + m);
            if (n - passed > mismatches.Count)
                Console.WriteLine("  ... and " + (n - passed - mismatches.Count) + " more.");
            return 1;
        }

        // Returns null on match, otherwise a human-readable description of the first diff.
        private static string Compare(JsonElement expected, JsonElement actual)
        {
            // Discrete string field.
            string emid = expected.GetProperty("missionId").GetString();
            string amid = actual.GetProperty("missionId").GetString();
            if (emid != amid) return "missionId " + emid + " != " + amid;

            // Discrete booleans.
            if (expected.GetProperty("routeValid").GetBoolean()
                != actual.GetProperty("routeValid").GetBoolean())
                return "routeValid differs";
            if (expected.GetProperty("totFeasible").GetBoolean()
                != actual.GetProperty("totFeasible").GetBoolean())
                return "totFeasible differs";
            if (expected.GetProperty("taskingGoNoGo").GetBoolean()
                != actual.GetProperty("taskingGoNoGo").GetBoolean())
                return "taskingGoNoGo differs";

            // Discrete integer (exact).
            long eTot = expected.GetProperty("estimatedTotEpochSec").GetInt64();
            long aTot = actual.GetProperty("estimatedTotEpochSec").GetInt64();
            if (eTot != aTot)
                return "estimatedTotEpochSec " + eTot + " != " + aTot;

            // Messages (exact, ordered).
            var em = expected.GetProperty("messages");
            var am = actual.GetProperty("messages");
            if (em.GetArrayLength() != am.GetArrayLength())
                return "messages length " + em.GetArrayLength() + " != " + am.GetArrayLength();
            {
                var ee = em.EnumerateArray();
                var ae = am.EnumerateArray();
                while (ee.MoveNext() && ae.MoveNext())
                {
                    if (ee.Current.GetString() != ae.Current.GetString())
                        return "message '" + ee.Current.GetString() + "' != '"
                            + ae.Current.GetString() + "'";
                }
            }

            // Continuous: totalDistanceNm within 1e-9 relative.
            double eTotal = expected.GetProperty("totalDistanceNm").GetDouble();
            double aTotal = actual.GetProperty("totalDistanceNm").GetDouble();
            if (RelErr(eTotal, aTotal) > RelTol)
                return "totalDistanceNm rel-err " + RelErr(eTotal, aTotal).ToString("E3",
                    CultureInfo.InvariantCulture) + " (" + eTotal + " vs " + aTotal + ")";

            // Continuous: legDistancesNm[i] within 1e-9 relative.
            var el = expected.GetProperty("legDistancesNm");
            var al = actual.GetProperty("legDistancesNm");
            if (el.GetArrayLength() != al.GetArrayLength())
                return "legDistancesNm length " + el.GetArrayLength() + " != " + al.GetArrayLength();
            {
                var ee = el.EnumerateArray();
                var ae = al.EnumerateArray();
                int i = 0;
                while (ee.MoveNext() && ae.MoveNext())
                {
                    double ev = ee.Current.GetDouble();
                    double av = ae.Current.GetDouble();
                    if (RelErr(ev, av) > RelTol)
                        return "legDistancesNm[" + i + "] rel-err " + RelErr(ev, av).ToString(
                            "E3", CultureInfo.InvariantCulture) + " (" + ev + " vs " + av + ")";
                    i++;
                }
            }

            return null;
        }

        private static double RelErr(double a, double b)
        {
            double denom = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-12);
            return Math.Abs(a - b) / denom;
        }

        // Walk up from the executable to find surrogate/corpus/corpus.json.
        private static string FindCorpus()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "corpus", "corpus.json");
                if (File.Exists(candidate)) return candidate;
                // also try a 'surrogate/corpus/corpus.json' under this dir
                string candidate2 = Path.Combine(dir, "surrogate", "corpus", "corpus.json");
                if (File.Exists(candidate2)) return candidate2;
                DirectoryInfo parent = Directory.GetParent(dir);
                dir = parent == null ? null : parent.FullName;
            }
            return null;
        }
    }
}
