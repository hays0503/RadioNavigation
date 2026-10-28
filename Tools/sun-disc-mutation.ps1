# Physical-disc / SH5 regression mutations. Never run concurrently with other builds.
# Each mutant starts from the SAME byte snapshot, compiles against the real game
# DLLs (C# 7), then runs both executable units and call-site guards. A compile
# error/empty run is a HARNESS ERROR, not a caught mutation. No game is started.
param(
    [string]$ReportDir = "artifacts/reports/solar-size-20261008/mutations",
    [switch]$Sh5,
    [switch]$CaptureGuard,
    [switch]$RenderProbe
)

$ErrorActionPreference = "Stop"
if (@($Sh5, $CaptureGuard, $RenderProbe | Where-Object { $_ }).Count -gt 1) { throw "Select only one mutation suite" }
$root = Split-Path -Parent $PSScriptRoot
$report = if ([IO.Path]::IsPathRooted($ReportDir)) { $ReportDir } else { Join-Path $root $ReportDir }
New-Item -ItemType Directory -Force -Path $report | Out-Null

$mutations = @(
    @{ Name="solar-x4"; File="Source/Logic/SunDiscScale.cs"; Old="return 2.0 * Math.Asin(sineRadius / distanceAu) / Deg2Rad;"; New="return 4.0625 * 2.0 * Math.Asin(sineRadius / distanceAu) / Deg2Rad;"; Expected="SolarSemidiameterMatchesTheUsers1939To1945Almanac" },
    @{ Name="interior-compactness-retuned"; File="Source/UI/SternspreizLensEffect.cs"; Old="const double interiorCompactDiameterDeg = 2.153125;"; New="const double interiorCompactDiameterDeg = 0.53;"; Expected="CelestialDisc_SolarSizeAndLimbUsePhysicalGeometry" },
    @{ Name="filter-gates-measuring-sun"; File="Source/UI/SextantController.cs"; Old="SunDiscRenderer.Active = true;"; New="SunDiscRenderer.Active = rightFilterCount > 0;"; Expected="CelestialDisc_SolarSizeAndLimbUsePhysicalGeometry" },
    @{ Name="limb-moved"; File="Source/Logic/CelestialDiscRaster.cs"; Old="0.5 + (LimbRadius - radius)"; New="0.0 + (LimbRadius - radius)"; Expected="PhysicalRadiusAndRasterHalfCoverageAreTheSameAngle" },
    @{ Name="quad-padding-lost"; File="Source/Logic/CelestialDiscRaster.cs"; Old="/ LimbRadius;"; New=";"; Expected="PhysicalRadiusAndRasterHalfCoverageAreTheSameAngle" },
    @{ Name="moon-x2"; File="Source/Logic/MoonDiscScale.cs"; Old="? 2.0 * Math.Asin(LunarRadiusKm / distance)"; New="? 4.0 * Math.Asin(LunarRadiusKm / distance)"; Expected="LunarDistanceIsTopocentricAndDiameterIsNotDoubled" },
    @{ Name="moon-dark-hole"; File="Source/Logic/MoonDiscRenderer.cs"; Old="byte alphaByte = (byte)(coverage[i] * 255f + 0.5f);"; New="byte alphaByte = (byte)(a * 255f + 0.5f);"; Expected="CelestialDisc_MoonSizeOpacityAndDiagnosticMatchTheRenderer" },
    @{ Name="body-gate-lost"; File="Source/Logic/SunDiscSighting.cs"; Old="return body.Kind == CelestialBodyKind.Sun && !body.IsNone ? this : None;"; New="return this;"; Expected="NonSolarSightsCannotStoreASolarDiscOrLimb" },
    @{ Name="capture-body-gate-lost"; File="Source/UI/SextantWorksheet.cs"; Old="disc = disc.ForBody(body);"; New=""; Expected="CelestialCapture_AppliesIndexOnlyAndBodyGatesStoredProvenance" },
    @{ Name="snapshot-zeroed"; File="Source/UI/SextantWorksheet.cs"; Old="obs.ObservedSunAngularDiameterDeg = disc.ObservedDiameterDeg;"; New="obs.ObservedSunAngularDiameterDeg = 0.0;"; Expected="CelestialDisc_CaptureProviderIsConnectedAndBodyGated" },
    @{ Name="provider-disconnected"; File="Source/UI/SextantController.cs"; Old="inputPoller.SunDiscSightingProvider = CaptureSunDiscSighting;"; New="inputPoller.SunDiscSightingProvider = null;"; Expected="CelestialDisc_CaptureProviderIsConnectedAndBodyGated" },
    @{ Name="provider-size-invented"; File="Source/UI/SextantController.cs"; Old="return solarOptics.Sighting(ready);"; New="return solarOptics.Sighting(false);"; Expected="CelestialDisc_CaptureProviderIsConnectedAndBodyGated" },
    @{ Name="native-sun-not-restored"; File="Source/Logic/CelestialRenderState.cs"; Old="sun.enabled = true;"; New="sun.enabled = false;"; Expected="NativeRendererStateIsRestoredEvenOnAnException" },
    @{ Name="partial-native-rollback-lost"; File="Source/Logic/CelestialRenderState.cs"; Old="state.Restore();"; New=""; Expected="PartialNativeSuppressionRollsBackBeforeTheCallerReceivesAToken" },
    @{ Name="replacement-mask-ignored"; File="Source/Logic/SextantCelestialRenderScopePatch.cs"; Old="&& (pip.cullingMask & (1 << renderer.gameObject.layer)) != 0"; New=""; Expected="CelestialDisc_NativeSuppressionIsScopedAndRestored" },
    @{ Name="finalizer-cleanup-lost"; File="Source/Logic/SextantCelestialRenderScopePatch.cs"; Old="__state.Restore();"; New=""; Expected="CelestialDisc_NativeSuppressionIsScopedAndRestored" },
    @{ Name="codec-size-lost"; File="Source/Logic/SextantSaveCodec.cs"; Old="w.Write(o.ObservedSunAngularDiameterDeg);"; New="w.Write(0.0);"; Expected="TheDiscSnapshotSurvivesASaveRoundTrip" },
    @{ Name="v1-rejected"; File="Source/Logic/SextantSaveCodec.cs"; Old="version >= 1 && version <= Version"; New="version >= Version && version <= Version"; Expected="GenuineV1PreservesTheFrozenSightAndCanBeSavedAsV3" },
    @{ Name="obsolete-offset-returned"; File="Source/UI/SextantWorksheet.cs"; Old="hoDeg, indexErrorArcMin));"; New="hoDeg + 0.8115625, indexErrorArcMin));"; Expected="CelestialCapture_AppliesIndexOnlyAndBodyGatesStoredProvenance" }
)

if ($Sh5) {
    $mutations = @(
        @{ Name="gha-rate-halved"; File="Source/Data/Sh5SunAlmanac.cs"; Old="hours * 15)"; New="hours * 7.5)"; Expected="Sh5_PaperFourHourDAndGhaIncrementsAreAppliedWithoutPortOrSkyShift" },
        @{ Name="d-treated-as-hourly"; File="Source/Data/Sh5SunAlmanac.cs"; Old="increments[index] / 600.0 * hours / 4"; New="increments[index] / 600.0 * hours"; Expected="Sh5_PaperFourHourDAndGhaIncrementsAreAppliedWithoutPortOrSkyShift" },
        @{ Name="port-hour-subtracted"; File="Source/Data/Sh5SunAlmanac.cs"; Old="+ hours * 15)"; New="+ (hours - 1) * 15)"; Expected="Sh5_PaperFourHourDAndGhaIncrementsAreAppliedWithoutPortOrSkyShift" },
        @{ Name="sundrift-reintroduced"; File="Source/Data/Sh5SunAlmanac.cs"; Old="int index = (utc.Date - start).Days"; New="utc = utc.AddMinutes(SunEphemerisCalibration.DeltaMinutes); int index = (utc.Date - start).Days"; Expected="Sh5_PaperFourHourDAndGhaIncrementsAreAppliedWithoutPortOrSkyShift" },
        @{ Name="low-cell-sign-lost"; File="Source/Logic/SolarHeightReduction.cs"; Old="{18,-1,-9,-15"; New="{18,1,-9,-15"; Expected="Sh5_FviiiMatchesAll810PrintedCellsAndMonthlyTerms" },
        @{ Name="printed-zero-smoothed"; File="Source/Logic/SolarHeightReduction.cs"; Old="{36,18,10,4,0,-5"; New="{36,18,10,4,-2,-5"; Expected="Sh5_FviiiPreservesPrintedZeroAndTwelveRatherThanSmoothingThem" },
        @{ Name="printed-twelve-smoothed"; File="Source/Logic/SolarHeightReduction.cs"; Old="{155,137,130,124,120,116"; New="{155,137,130,124,119,116"; Expected="Sh5_FviiiPreservesPrintedZeroAndTwelveRatherThanSmoothingThem" },
        @{ Name="monthly-term-lost"; File="Source/Logic/SolarHeightReduction.cs"; Old="monthArcMin = monthly[month - 1] / 10.0;"; New="monthArcMin = 0;"; Expected="Sh5_FviiiUsesIndexBeforeInterpolationAndMonthlyTermExactlyOnce" },
        @{ Name="duplicate-sd"; File="Source/Logic/SolarHeightReduction.cs"; Old="hoDeg = h + correctionArcMin / 60;"; New="hoDeg = h + (correctionArcMin + 16) / 60;"; Expected="Sh5_FviiiUsesIndexBeforeInterpolationAndMonthlyTermExactlyOnce" },
        @{ Name="index-minutes-as-degrees"; File="Source/Logic/SolarHeightReduction.cs"; Old="double h = rawHsDeg + indexArcMin / 60;"; New="double h = rawHsDeg + indexArcMin;"; Expected="Sh5_FviiiUsesIndexBeforeInterpolationAndMonthlyTermExactlyOnce" },
        @{ Name="upper-limb-accepted"; File="Source/Logic/SolarHeightReduction.cs"; Old="limb != SunSightLimb.LowerLimb || !Finite(rawHsDeg)"; New="!Finite(rawHsDeg)"; Expected="Sh5_InvalidReductionIsRefusedNotClamped" },
        @{ Name="radius-doubled-in-optics"; File="Source/Logic/Sh5SolarNavigation.cs"; Old="horizonAltitudeDeg + lower + diameterDeg / 2"; New="horizonAltitudeDeg + lower + diameterDeg"; Expected="Sh5_ActualOpticsUsesMeanWaterAndIgnoresLegacySkyCalibration" },
        @{ Name="world-y-as-eye-height"; File="Source/UI/SextantSolarOptics.cs"; Old="camera.transform.position.y - water.transform.position.y"; New="camera.transform.position.y"; Expected="Sh5_ActualOpticsUsesMeanWaterAndIgnoresLegacySkyCalibration" },
        @{ Name="wave-y-as-mean-sea"; File="Source/UI/SextantSolarOptics.cs"; Old="camera.transform.position.y - water.transform.position.y"; New="camera.transform.position.y - waterCamera.WaterLevel"; Expected="Sh5_ActualOpticsUsesMeanWaterAndIgnoresLegacySkyCalibration" },
        @{ Name="real-earth-dip-substituted"; File="Source/Logic/SolarSeaHorizon.cs"; Old="altitudeDeg = Math.Atan(slope) * 180 / Math.PI;"; New="altitudeDeg = -1.76 * Math.Sqrt(eyeHeightM) / 60;"; Expected="Sh5_HorizonUsesGameShaderNotRealEarthOrCameraWorldHeight" },
        @{ Name="renderer-direction-disconnected"; File="Source/UI/SextantController.cs"; Old="SunDiscRenderer.SunDirection = solarOptics.Direction;"; New="SunDiscRenderer.SunDirection = Vector3.zero;"; Expected="Sh5_OpticalAdapterFeedsTheLateRendererWithPlainUtcAndMeanWater" },
        @{ Name="source-time-not-frozen"; File="Source/UI/SextantWorksheet.cs"; Old="utc = disc.ConfiguredUtc;"; New="utc = GameTimeResolver.GetUtcNow();"; Expected="Sh5_ActualCaptureFreezesRawIndexEyeClockAndModelThroughSave" },
        @{ Name="source-position-not-frozen"; File="Source/UI/SextantWorksheet.cs"; Old="lat = disc.TableRequested ? disc.ObserverLatDeg : latF;"; New="lat = latF;"; Expected="Sh5_CaptureKeepsTheExactConfiguredPositionBeforeBuildingHc" },
        @{ Name="unknown-sun-falls-back"; File="Source/UI/SextantWorksheet.cs"; Old="if (body.Kind == CelestialBodyKind.Sun)"; New="if (body.Kind == CelestialBodyKind.Sun && disc.TableRequested)"; Expected="Sh5_NewSunCannotUseTheLegacyUnsourcedCaptureOverload" },
        @{ Name="completed-legacy-series-mixed"; File="Source/UI/SextantWorksheet.cs"; Old="if (HasLegacySun(runningFix.Observation1) || HasLegacySun(runningFix.Observation2))"; New="if (!runningFix.HasThird && (HasLegacySun(runningFix.Observation1) || HasLegacySun(runningFix.Observation2)))"; Expected="Sh5_NoLegacySolarSeriesCanAcceptANewTableSun" },
        @{ Name="renderer-readiness-ignored"; File="Source/UI/SextantSolarOptics.cs"; Old="TableCompatible = Ready && rendererReady"; New="TableCompatible = Ready"; Expected="Sh5_ActualOpticsUsesMeanWaterAndIgnoresLegacySkyCalibration" },
        @{ Name="debug-index-wrong-units"; File="Source/UI/SextantStarId.cs"; Old="bodyAlt - SunDiscSighting.ApplyMeasuredChain(arcAngle, IndexErrorCorrection.Load())"; New="bodyAlt - (arcAngle + IndexErrorCorrection.Load())"; Expected="Sh5_DebugIndexUnitsAreArcminutesForSolarAndOtherBodies" },
        @{ Name="frozen-gha-refinement-lost"; File="Source/Logic/SextantRunningFix.cs"; Old="observation.SolarReductionVersion == 1"; New="observation.SolarReductionVersion == 2"; Expected="Sh5_RunningSolarFixRefinesFrozenGhaNotTheLiveCalibratedRa" },
        @{ Name="raw-hs-save-lost"; File="Source/Logic/SextantSaveCodec.cs"; Old="w.Write(o.RawHsDeg);"; New="w.Write(0.0);"; Expected="Sh5_ActualCaptureFreezesRawIndexEyeClockAndModelThroughSave" },
        @{ Name="v3-semantic-validation-lost"; File="Source/Logic/SextantSaveCodec.cs"; Old="ValidateSolarReduction(o);"; New=""; Expected="Sh5_CorruptV3PayloadIsRejectedTransactionally" },
        @{ Name="frozen-load-requires-live-table"; File="Source/Logic/SextantSaveCodec.cs"; Old="ValidateSolarChain(o);"; New='ValidateSolarChain(o); Require(Sh5SunAlmanac.Loaded, "missing live almanac");'; Expected="Sh5_V3DecoderPreservesFrozenSightWithNoLiveAlmanacAvailable" },
        @{ Name="legacy-v2-rejected"; File="Source/Logic/SextantSaveCodec.cs"; Old="version >= 1 && version <= Version"; New="version >= 3 && version <= Version"; Expected="GenuineV2PreservesLegacyHoAndDiscWithoutInventingASh5Reduction" }
    )
}
elseif ($CaptureGuard) {
    # The no-HIP gate must follow the actual helper call and failed result,
    # not a disconnected Compute token or a check on an invented Sun identity.
    $mutations = @(
        @{ Name="frame-validates-wrong-body"; File="Source/UI/SextantWorksheet.cs"; Old="TryCaptureFrame(body, disc, out utc, out lat, out lon)"; New="TryCaptureFrame(CelestialBody.Sun(), disc, out utc, out lat, out lon)"; Expected="SextantStarId_SkipsNoHipSentinel" },
        @{ Name="ephemeris-validates-wrong-body"; File="Source/UI/SextantWorksheet.cs"; Old="!CelestialEphemeris.Compute(body, utc, out _, out _)"; New="!CelestialEphemeris.Compute(CelestialBody.Sun(), utc, out _, out _)"; Expected="SextantStarId_SkipsNoHipSentinel" },
        @{ Name="unknown-ephemeris-accepted"; File="Source/UI/SextantWorksheet.cs"; Old="LogPrefix, body.Kind, body.Hip));`n                return false;"; New="LogPrefix, body.Kind, body.Hip));`n                return true;"; Expected="SextantStarId_SkipsNoHipSentinel" },
        @{ Name="frame-failure-ignored"; File="Source/UI/SextantWorksheet.cs"; Old="if (!TryCaptureFrame(body, disc, out utc, out lat, out lon))"; New="if (TryCaptureFrame(body, disc, out utc, out lat, out lon) && body.IsNone)"; Expected="SextantStarId_SkipsNoHipSentinel" }
    )
}
elseif ($RenderProbe) {
    $mutations = @(
        @{ Name="safe-open-calibrates"; File="Source/UI/SextantController.cs"; Old="renderOnlyProbe = true; // gate writes BEFORE making the overlay live"; New="renderOnlyProbe = true; calibrator.Solve(positionProvider, gameSun, worksheet); // deliberately unsafe mutant"; Expected="RenderProbe_EmptyAndRetainedSeriesNeverEnterAutomaticCalibration" },
        @{ Name="public-entry-opens-normally"; File="Source/UI/SextantProbe.cs"; Old="if (!controller.TryOpenForRenderProbe())"; New="if (!controller.TryOpenForProbe())"; Expected="RenderProbe_EmptyAndRetainedSeriesNeverEnterAutomaticCalibration" },
        @{ Name="mode-not-armed"; File="Source/UI/SextantController.cs"; Old="renderOnlyProbe = true; // gate writes BEFORE making the overlay live"; New="renderOnlyProbe = false; // deliberately unsafe mutant"; Expected="RenderProbe_EmptyAndRetainedSeriesNeverEnterAutomaticCalibration" },
        @{ Name="mode-armed-too-late"; File="Source/UI/SextantController.cs"; Old="renderOnlyProbe = true; // gate writes BEFORE making the overlay live`n            FinishOpen();"; New="FinishOpen();`n            renderOnlyProbe = true;"; Expected="RenderProbe_EmptyAndRetainedSeriesNeverEnterAutomaticCalibration" },
        @{ Name="shared-helper-calibrates"; File="Source/UI/SextantController.cs"; Old="private void FinishOpen()`n        {`n            ResetReadingState();"; New="private void FinishOpen()`n        {`n            calibrator.Solve(positionProvider, gameSun, worksheet);`n            ResetReadingState();"; Expected="RenderProbe_SharedOpenHelpersContainNoHiddenCalibrationWrites" },
        @{ Name="key-write-gate-lost"; File="Source/UI/SextantController.cs"; Old="if (!renderOnlyProbe)`n                {`n                    inputPoller.PollFix(arcAngle);"; New="if (IsOpen)`n                {`n                    inputPoller.PollFix(arcAngle);"; Expected="RenderProbe_UpdateBlocksWorksheetAndIndexButKeepsRenderControls" },
        @{ Name="index-write-outside-gate"; File="Source/UI/SextantController.cs"; Old="inputPoller.PollIndexError(arcAngle);`n                }`n                if (pPressed)"; New="}`n                inputPoller.PollIndexError(arcAngle);`n                if (pPressed)"; Expected="RenderProbe_UpdateBlocksWorksheetAndIndexButKeepsRenderControls" },
        @{ Name="course-track-write-gate-lost"; File="Source/UI/SextantController.cs"; Old="if (!renderOnlyProbe)`n                    worksheet.UpdateCourseSampler(Time.unscaledDeltaTime);"; New="worksheet.UpdateCourseSampler(Time.unscaledDeltaTime);"; Expected="RenderProbe_UpdateBlocksWorksheetAndIndexButKeepsRenderControls" },
        @{ Name="record-write-gate-lost"; File="Source/UI/SextantProbe.cs"; Old='if (controller.RenderOnlyForProbe)' + "`n" + '                return "{\"ok\":false,\"error\":\"render-only\"}";' + "`n" + '            if (!SextantController.IsOpen)'; New='if (!SextantController.IsOpen)'; Expected="RenderProbe_RecordAndResetRefuseBeforeReadingOrChangingSights" },
        @{ Name="reset-write-gate-lost"; File="Source/UI/SextantProbe.cs"; Old='if (controller.RenderOnlyForProbe)' + "`n" + '                return "{\"ok\":false,\"error\":\"render-only\"}";' + "`n" + '            controller.WorksheetForProbe.ResetSeries();'; New='controller.WorksheetForProbe.ResetSeries();'; Expected="RenderProbe_RecordAndResetRefuseBeforeReadingOrChangingSights" },
        @{ Name="close-mode-release-lost"; File="Source/UI/SextantController.cs"; Old="renderOnlyProbe = false;`n            openPoseLogPending = false;"; New="openPoseLogPending = false;"; Expected="RenderProbe_CloseClearsTheModeAndNormalReopenRemainsWritable" },
        @{ Name="ordinary-open-falsely-certified"; File="Source/UI/SextantController.cs"; Old="if (IsOpen)`n                return renderOnlyProbe;"; New="if (IsOpen)`n                return true;"; Expected="RenderProbe_AlreadyOrdinaryOpenIsNotCertifiedAsCalibrationFree" },
        @{ Name="failed-attach-falsely-certified"; File="Source/UI/SextantController.cs"; Old="return IsOpen && renderOnlyProbe;"; New="return renderOnlyProbe;"; Expected="RenderProbe_FailedOpenDoesNotCalibrateOrClaimRendering" },
        @{ Name="normal-calibration-disconnected"; File="Source/UI/SextantController.cs"; Old="calibrator.Solve(positionProvider, gameSun, worksheet);"; New=""; Expected="RenderProbe_OrdinaryOpenStillUsesCalibrationAndWritableActions" },
        @{ Name="probe-stops-edge-sampling"; File="Source/UI/SextantController.cs"; Old="inputPoller.SampleEdges(IsOpen);"; New="if (!renderOnlyProbe) inputPoller.SampleEdges(IsOpen);"; Expected="RenderProbe_UpdateBlocksWorksheetAndIndexButKeepsRenderControls" }
    )
}

$files = @($mutations.File | Sort-Object -Unique)
$backup = @{}
foreach ($file in $files) {
    $path = Join-Path $root $file
    $backup[$file] = @{ Bytes=[IO.File]::ReadAllBytes($path); Mtime=(Get-Item $path).LastWriteTimeUtc; Hash=(Get-FileHash $path -Algorithm SHA256).Hash }
}

function Restore-Sources {
    foreach ($file in $files) {
        $path = Join-Path $root $file
        [IO.File]::WriteAllBytes($path, $backup[$file].Bytes)
        (Get-Item $path).LastWriteTimeUtc = $backup[$file].Mtime
    }
}

function Invoke-Checked([string]$name, [string]$exe, [string[]]$arguments) {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = "Continue" # Native stderr is data; exit code owns success.
    try {
        $output = (& $exe @arguments 2>&1 | Out-String)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $saved }
    [IO.File]::WriteAllText((Join-Path $report ($name + ".log")), $output)
    return @{ Code=$code; Output=$output }
}

function Run-Checks([string]$name) {
    # Skip PVS ONLY for fault injection. The restored tree must pass the normal
    # build/PVS gate separately. This call proves all mutants are valid C# 7.
    $compile = Invoke-Checked ($name + "-compile") "powershell" @("-ExecutionPolicy", "Bypass", "-File", (Join-Path $root "Tools/build.ps1"), "-SkipPvs")
    if ($compile.Code -ne 0) { throw "$name did not compile (not a caught mutation)" }
    $unitsBuild = Invoke-Checked ($name + "-unit-build") "dotnet" @("build", "Dev/Tests/Runner/Runner.csproj", "-c", "Release", "-t:Rebuild", "--nologo")
    if ($unitsBuild.Code -ne 0) { throw "$name unit harness did not compile" }
    $units = Invoke-Checked ($name + "-units") "dotnet" @("run", "--project", "Dev/Tests/Runner/Runner.csproj", "-c", "Release", "--no-build")
    $filter = if ($Sh5) { "Sh5_" } elseif ($CaptureGuard) { "SextantStarId_SkipsNoHipSentinel" } elseif ($RenderProbe) { "RenderProbe_" } else { "CelestialDisc" }
    $gate = Invoke-Checked ($name + "-gate") "dotnet" @("run", "--project", "Dev/RadioNavigation.SourceCheck", "-c", "Release", "--no-build", "--", $filter)
    foreach ($run in @($units, $gate)) {
        if ($run.Output -notmatch "Passed: (\d+), Failed: (\d+)" -or [int]$Matches[1] + [int]$Matches[2] -eq 0) {
            throw "$name empty/crashed test harness (not a caught mutation)"
        }
        if ($run.Code -ne 0 -and $run.Output -notmatch "(?m)^FAIL\s") { throw "$name test process crashed" }
    }
    return @{ Units=$units; Gate=$gate }
}

$results = @()
$env:DOTNET_TieredCompilation = "0"
Push-Location $root
try {
    $buildGate = Invoke-Checked "gate-build" "dotnet" @("build", "Dev/RadioNavigation.SourceCheck", "-c", "Release", "-t:Rebuild", "--nologo")
    if ($buildGate.Code -ne 0) { throw "SourceCheck harness failed to build" }
    $base = Run-Checks "baseline"
    if ($base.Units.Code -ne 0 -or $base.Gate.Code -ne 0) { throw "Baseline is not green; mutations aborted" }
    Write-Host "BASELINE GREEN"
    foreach ($m in $mutations) {
        Restore-Sources # ALL prior mutations removed BEFORE every case.
        $path = Join-Path $root $m.File
        $text = [IO.File]::ReadAllText($path)
        $count = ([regex]::Matches($text, [regex]::Escape($m.Old))).Count
        if ($count -ne 1) { throw "Ambiguous/missing anchor in $($m.Name): $count" }
        [IO.File]::WriteAllText($path, $text.Replace($m.Old, $m.New), [Text.UTF8Encoding]::new($false))
        try {
            $run = Run-Checks $m.Name
            $combined = $run.Units.Output + "`n" + $run.Gate.Output
            $caught = $combined -match ("(?m)^FAIL\s+" + [regex]::Escape($m.Expected) + "\s*$")
            $results += [pscustomobject]@{ Name=$m.Name; Compiled=$true; Caught=$caught; ExpectedFail=$m.Expected }
            Write-Host ("{0}: {1}" -f $m.Name, $(if ($caught) { "CAUGHT" } else { "SURVIVED" }))
        } finally { Restore-Sources }
    }
    $restored = Run-Checks "restored"
    if ($restored.Units.Code -ne 0 -or $restored.Gate.Code -ne 0) { throw "Restored baseline is not green" }
} finally {
    Restore-Sources
    $results | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $report "results.json") -Encoding UTF8
    $receipt = @()
    foreach ($file in $files) {
        $path = Join-Path $root $file
        $item = Get-Item $path
        $hash = (Get-FileHash $path -Algorithm SHA256).Hash
        $receipt += [pscustomobject]@{ File=$file; Sha256=$backup[$file].Hash; RestoredSha256=$hash; Length=$backup[$file].Bytes.Length; RestoredLength=$item.Length; MtimeUtc=$backup[$file].Mtime.ToString("o"); RestoredMtimeUtc=$item.LastWriteTimeUtc.ToString("o") }
        if ($hash -ne $backup[$file].Hash -or $item.Length -ne $backup[$file].Bytes.Length -or $item.LastWriteTimeUtc -ne $backup[$file].Mtime) { throw "Restoration byte/timestamp mismatch: $file" }
    }
    $receipt | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $report "restoration.json") -Encoding UTF8
    Pop-Location
}
$survivors = @($results | Where-Object { -not $_.Caught })
if ($survivors.Count -gt 0 -or $results.Count -ne $mutations.Count) { throw "Mutation verification incomplete or survivors present" }
Write-Host "ALL $($results.Count) MUTATIONS CAUGHT; byte hashes and timestamps restored"
