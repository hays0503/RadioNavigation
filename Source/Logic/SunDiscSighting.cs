namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// What the player was actually looking at when a sight was taken: the
    /// solar disc as DRAWN at that instant, and which limb.
    ///
    /// This is a SNAPSHOT, captured at the key press and passed into
    /// <see cref="SextantWorksheet.CaptureObservation"/>. It is deliberately
    /// not re-read from the renderer later: the player may sight the Sun with
    /// the filter up, drop the filter, and only then open the worksheet. If the
    /// diameter were evaluated against live state at that later moment the
    /// recorded altitude would change under the player — the record would stop
    /// being a record.
    ///
    /// Pure data, no Unity — unit-tested offline.
    /// </summary>
    public struct SunDiscSighting
    {
        /// <summary>
        /// Angular diameter of the disc as drawn at the sight (degrees), or 0
        /// when no solar disc took part in the sight.
        /// </summary>
        public double ObservedDiameterDeg;

        /// <summary>Which limb was brought to the horizon.</summary>
        public SunSightLimb Limb;

        // New Sun captures require this model. Only decoded frozen v1/v2
        // records retain the old convention; unsourced callers cannot record.
        public bool TableRequested;
        public bool TableCompatible;
        public double EyeHeightM;
        public double HorizonAltitudeDeg;
        public double ExpectedHsDeg;
        public System.DateTime ConfiguredUtc;
        public double ObserverLatDeg;
        public double ObserverLonDeg;

        /// <summary>No known measuring solar disc (including non-solar sights).</summary>
        public static SunDiscSighting None
        {
            get { return new SunDiscSighting(); }
        }

        /// <summary>
        /// Normalises provenance at the capture boundary. A Moon/star can never
        /// carry a solar disc, even if an adapter incorrectly supplied one.
        /// </summary>
        public SunDiscSighting ForBody(CelestialBody body)
        {
            return body.Kind == CelestialBodyKind.Sun && !body.IsNone ? this : None;
        }

        /// <summary>
        /// The diameter actually configured for the measuring renderer. No
        /// seasonal variation is treated as a rendering error; it is real.
        /// </summary>
        public static SunDiscSighting TrueSize(double diameterDeg)
        {
            return new SunDiscSighting
            {
                ObservedDiameterDeg = diameterDeg,
                Limb = SunSightLimb.LowerLimb
            };
        }

        /// <summary>
        /// New discs are physical size: only index error changes the raw drum
        /// reading here. Historical SD/dip/refraction must be reduced separately,
        /// not silently guessed from the render. Previously frozen Ho and v2
        /// compensation fields are preserved by the save codec, never recomputed.
        /// </summary>
        public static double ApplyMeasuredChain(double rawArcDeg, double indexErrorArcMin)
        {
            return rawArcDeg + indexErrorArcMin / 60.0;
        }
    }
}
