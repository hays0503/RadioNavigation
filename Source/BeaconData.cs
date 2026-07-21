namespace UBOAT.Mods.TestUI
{
    public struct BeaconInfo
    {
        public string Name;
        public string Country;
        public string Callsign;
        public double Latitude;
        public double Longitude;
        public string ActivationDate;
        public float FrequencyKHz;
    }

    public static class BeaconData
    {
        public static readonly BeaconInfo[] Beacons =
        {
            new BeaconInfo
            {
                Name = "Ploneis (Quimper)",
                Country = "France",
                Callsign = "FRQ",
                Latitude = 48.018333,
                Longitude = -4.215278,
                ActivationDate = "1942-06-01",
                FrequencyKHz = 257.0f
            },
            new BeaconInfo
            {
                Name = "Stavanger–Varhaug",
                Country = "Norway",
                Callsign = "LEC",
                Latitude = 58.625556,
                Longitude = 5.630278,
                ActivationDate = "1942-06-01",
                FrequencyKHz = 319.0f
            },
            new BeaconInfo
            {
                Name = "Lugo (Outeiro de Rei)",
                Country = "Spain",
                Callsign = "LG",
                Latitude = 43.248056,
                Longitude = -7.482222,
                ActivationDate = "1942-06-01",
                FrequencyKHz = 285.0f
            },
            new BeaconInfo
            {
                Name = "Sevilla",
                Country = "Spain",
                Callsign = "SL",
                Latitude = 37.521389,
                Longitude = -6.021667,
                ActivationDate = "1942-06-01",
                FrequencyKHz = 315.0f
            },
            new BeaconInfo
            {
                Name = "Andøya",
                Country = "Norway",
                Callsign = "LEX",
                Latitude = 69.148333,
                Longitude = 15.883333,
                ActivationDate = "1943-04-01",
                FrequencyKHz = 332.5f
            },
            new BeaconInfo
            {
                Name = "Bjørnøya",
                Country = "Norway",
                Callsign = "LJS",
                Latitude = 74.492778,
                Longitude = 19.059722,
                ActivationDate = "1943-06-01",
                FrequencyKHz = 332.5f
            },
            new BeaconInfo
            {
                Name = "Jan Mayen",
                Country = "Norway",
                Callsign = "LMC",
                Latitude = 71.050000,
                Longitude = -8.233333,
                ActivationDate = "1943-08-01",
                FrequencyKHz = 332.5f
            }
        };
    }
}
