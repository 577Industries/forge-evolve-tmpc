Attribute VB_Name = "GeoFixedPoint"
' ─────────────────────────────────────────────────────────────────────────────
' GeoFixedPoint.bas — SYNTHETIC, INTENTIONALLY-LEGACY VB6 fixed-point geo module.
'
' PART OF: FORGE EVOLVE for TMPC, synthetic unclassified surrogate (Phase 1).
'
' This module models the kind of decades-old VB6 "mil-grid" coordinate math that lingers
' in legacy mission-planning tool-chains: scaled-integer (fixed-point) lat/lon stored as
' "mils" with manual overflow-prone arithmetic, GoTo error handling, and Variant typing.
' It is 100% synthetic and unclassified — NOT a real grid, datum, or algorithm.
'
' It is NOT invoked by the C# processing path. It is present so the discovery / VB6-grammar
' demo has a representative VB6 artifact to parse and so the modernization can show a
' VB6 -> TypeScript lift.
'
' NOTE: VB6 -> TypeScript is one of FORGE EVOLVE's ALREADY-VALIDATED transformation paths
' (the framework's prior published results cover COBOL/Fortran/Ada/VB6 -> Java/Python/
' Rust/TypeScript). The C#/.NET capability is the new TMPC-specific extension; this VB6
' file lets the surrogate exercise the pre-existing VB6 front-end.
' ─────────────────────────────────────────────────────────────────────────────
Option Explicit

' Fixed-point scale: 1 degree == 10000 "mil-grid" units (synthetic).
Private Const MILGRID_SCALE As Long = 10000
Private Const DEG_MAX As Long = 180
Private Const MILGRID_WRAP As Long = 360 * MILGRID_SCALE   ' 3,600,000

' Static module state (a classic VB6 global-state smell): last conversion error code.
Public gLastGeoError As Integer

' Convert a floating-point degree value to scaled fixed-point "mil-grid" units.
' Deliberately uses CLng (banker's rounding) and no range guard beyond a manual check.
Public Function DegreesToMilGrid(ByVal degValue As Double) As Long
    On Error GoTo Handler
    gLastGeoError = 0
    If degValue > DEG_MAX Or degValue < -DEG_MAX Then
        gLastGeoError = 1            ' out-of-range; caller is expected to check (often doesn't)
    End If
    DegreesToMilGrid = CLng(degValue * MILGRID_SCALE)
    Exit Function
Handler:
    gLastGeoError = 99
    DegreesToMilGrid = 0
End Function

' Convert scaled fixed-point "mil-grid" units back to degrees.
Public Function MilGridToDegrees(ByVal grid As Long) As Double
    MilGridToDegrees = CDbl(grid) / CDbl(MILGRID_SCALE)
End Function

' Legacy "longitude delta" in mil-grid units. NOTE the SAME anti-meridian class of bug as
' the C# D1 defect: it subtracts raw grid values and only half-heartedly wraps, so a delta
' that crosses +/-180 can come out near 3,600,000 instead of small. Present to show the
' discovery engine the rule is DUPLICATED (and drifted) across languages.
Public Function MilGridLonDelta(ByVal lon1Grid As Long, ByVal lon2Grid As Long) As Long
    Dim d As Long
    d = lon2Grid - lon1Grid
    ' (Intentionally incomplete wrap: only handles one direction, like a lot of real code.)
    If d > MILGRID_WRAP \ 2 Then
        d = d - MILGRID_WRAP
    End If
    MilGridLonDelta = d
End Function

' A coarse "is this leg inside the degree box" check in fixed point, mirroring the
' C#/SQL feasibility proxy. Uses a magic literal (22 deg == 220000 mil-grid units).
Public Function LegInsideBox(ByVal lat1Grid As Long, ByVal lon1Grid As Long, _
                             ByVal lat2Grid As Long, ByVal lon2Grid As Long) As Boolean
    Dim dLat As Long
    Dim dLon As Long
    dLat = Abs(lat2Grid - lat1Grid)
    dLon = Abs(MilGridLonDelta(lon1Grid, lon2Grid))
    If dLat <= 220000 And dLon <= 220000 Then
        LegInsideBox = True
    Else
        LegInsideBox = False
    End If
End Function
