/*
 * mission-review.js — SYNTHETIC, INTENTIONALLY-LEGACY jQuery mission-review UI.
 *
 * PART OF: FORGE EVOLVE for TMPC, synthetic unclassified surrogate (Phase 1).
 *
 * Models the classic client-side technical debt found in legacy mission-planning web UIs:
 * an IIFE-wrapped jQuery widget with inline DOM string-building and a BUSINESS RULE
 * RE-IMPLEMENTED ON THE CLIENT that has DRIFTED out of sync with the authoritative
 * server-side rule. 100% synthetic and unclassified — no real data, no real algorithm.
 *
 * THE DRIFT BUG (representative of real "the JS copy is stale" defects):
 *   The authoritative server rule (see MissionProcessor.cs) is:
 *       GO  iff  routeValid AND NOT (variant === "MST" AND platform === "SSN")
 *   i.e. MST is SURFACE-ONLY, so MST-on-SSN must be NO-GO.
 *   The client copy below was forked from an OLDER server version that predates the
 *   MST-surface-only constraint, so it WRONGLY shows GO for MST on SSN. The UI therefore
 *   green-lights a tasking the server would (correctly) reject — a silent client/server
 *   divergence the modernization is meant to surface and eliminate.
 *
 * Loaded by a (notional) mission-review.html. Depends on a global jQuery ($).
 */
(function ($) {
    "use strict";

    // Magic constants duplicated from the server (another smell: no shared contract).
    var TOT_TOL_SEC = 120;
    var WEAPON_MAX_RANGE_NM = { BlockIV: 900, BlockV: 1000, MST: 1000 };

    /*
     * STALE client copy of the tasking rule. NOTE: it is missing the
     * (variant === "MST" && platform === "SSN") => NO-GO clause. This is the drift bug.
     */
    function clientTaskingGoNoGo(result, request) {
        if (!result.routeValid) {
            return false;
        }
        // BUG: the MST-surface-only constraint that the server enforces is absent here.
        // (An older spec only gated on routeValid.) Left as-is to represent rule drift.
        return true;
    }

    function clientTotFeasible(result, request) {
        var delta = Math.abs(result.estimatedTotEpochSec - request.desiredTotEpochSec);
        return delta <= TOT_TOL_SEC;
    }

    // Inline DOM string-building (no templating, no escaping discipline — legacy smell).
    function renderRow(label, value, cssClass) {
        return '<tr class="' + (cssClass || "") + '">' +
            '<td class="mr-label">' + label + '</td>' +
            '<td class="mr-value">' + value + '</td>' +
            '</tr>';
    }

    function renderMissionReview(request, result, $container) {
        var go = clientTaskingGoNoGo(result, request);          // uses the DRIFTED rule
        var feasible = clientTotFeasible(result, request);
        var range = WEAPON_MAX_RANGE_NM[request.variant] || 0;

        var html = '<table class="mission-review">';
        html += renderRow("Mission", request.missionId, "");
        html += renderRow("Platform / Variant",
            request.platform + " / " + request.variant, "");
        html += renderRow("Total Distance (nm)",
            (result.totalDistanceNm || 0).toFixed(2), "");
        html += renderRow("Route Valid", result.routeValid ? "YES" : "NO",
            result.routeValid ? "ok" : "bad");
        html += renderRow("Weapon Max Range (nm)", range, "");
        html += renderRow("TOT Feasible", feasible ? "YES" : "NO",
            feasible ? "ok" : "bad");
        // The drift surfaces here: client GO can disagree with server taskingGoNoGo.
        html += renderRow("Tasking (client view)", go ? "GO" : "NO-GO",
            go ? "go" : "nogo");
        if (typeof result.taskingGoNoGo === "boolean" && result.taskingGoNoGo !== go) {
            html += renderRow("WARNING",
                "client/server tasking disagree (stale client rule)", "warn");
        }
        html += '</table>';

        $container.html(html);
    }

    // Public-ish surface attached to the jQuery namespace (legacy plugin pattern).
    $.missionReview = function (request, result, selector) {
        var $c = $(selector);
        if ($c.length === 0) {
            return;
        }
        renderMissionReview(request, result, $c);
    };

    // Auto-wire any element with data-mission-review on DOM ready.
    $(function () {
        $("[data-mission-review]").each(function () {
            var $el = $(this);
            var req = $el.data("request");
            var res = $el.data("result");
            if (req && res) {
                renderMissionReview(req, res, $el);
            }
        });
    });

})(window.jQuery);
