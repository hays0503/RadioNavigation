namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Kind of celestial body the sextant can sight (SEXTANT_PLAN v2.2).
    /// Stars and the Moon are rendered by the mod's own ephemeris; the Sun is
    /// rendered by the game and therefore carries the SUNDRIFT time shift.
    /// </summary>
    public enum CelestialBodyKind
    {
        None = 0,
        Sun = 1,
        Moon = 2,
        Star = 3
    }

    /// <summary>
    /// A sightable body identity carried from identification
    /// (SextantStarId) into the worksheet observation. Pure value type: the
    /// ephemeris itself is resolved by CelestialEphemeris at record time.
    /// Cyrillic display names are u-escaped like the rest of the mod (game
    /// compiler encoding safety); AsciiName feeds the map tooltip, whose TMP
    /// font has no Cyrillic glyphs.
    /// </summary>
    public struct CelestialBody
    {
        public CelestialBodyKind Kind;
        public string Name;
        public int Hip;

        public bool IsNone
        {
            get { return Kind == CelestialBodyKind.None; }
        }

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(Name) ? "?" : Name; }
        }

        /// <summary>Latin-only name for the nav-map tooltip (TMP font).</summary>
        public string AsciiName
        {
            get
            {
                switch (Kind)
                {
                    case CelestialBodyKind.Sun: return "Sun";
                    case CelestialBodyKind.Moon: return "Moon";
                    default: return DisplayName;
                }
            }
        }

        public static CelestialBody None()
        {
            return new CelestialBody();
        }

        public static CelestialBody Sun()
        {
            CelestialBody body = new CelestialBody();
            body.Kind = CelestialBodyKind.Sun;
            body.Name = "\u0421\u043E\u043B\u043D\u0446\u0435";
            return body;
        }

        public static CelestialBody Moon()
        {
            CelestialBody body = new CelestialBody();
            body.Kind = CelestialBodyKind.Moon;
            body.Name = "\u041B\u0443\u043D\u0430";
            return body;
        }

        public static CelestialBody Star(int hip, string name)
        {
            CelestialBody body = new CelestialBody();
            body.Kind = CelestialBodyKind.Star;
            body.Hip = hip;
            body.Name = name;
            return body;
        }
    }
}
