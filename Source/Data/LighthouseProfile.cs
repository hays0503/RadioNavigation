using System;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Static profile of one lighthouse placed by the mod. Historical seed
    /// data (name, coordinates, fire height, light characteristic) comes from
    /// the Leuchtfeuer registry — Source/Data/Leuchtfeuer.json — converted
    /// into C# blocks by Tools/Import-Leuchtfeuer.ps1.
    ///
    /// Rotation fields are mod-specific: the registry only carries the
    /// flashing characteristic, not the beam rotation period, so the rotation
    /// speed is configured per profile here.
    /// </summary>
    public sealed class LighthouseProfile
    {
        public string Code;
        public string Name;
        public string Country;
        public string Location;
        public float Latitude;
        public float Longitude;
        public float FireHeightM;
        public string Characteristic;
        public string Type;
        public string PrefabPath;
        public string RotatorObjectName;
        public float RotationDegreesPerSecond;
        public bool Enabled;

        public LighthouseProfile(
            string code,
            string name,
            string country,
            string location,
            float latitude,
            float longitude,
            float fireHeightM,
            string characteristic,
            string type,
            string prefabPath,
            string rotatorObjectName,
            float rotationDegreesPerSecond,
            bool enabled)
        {
            Code = code;
            Name = name;
            Country = country;
            Location = location;
            Latitude = latitude;
            Longitude = longitude;
            FireHeightM = fireHeightM;
            Characteristic = characteristic;
            Type = type;
            PrefabPath = prefabPath;
            RotatorObjectName = rotatorObjectName;
            RotationDegreesPerSecond = rotationDegreesPerSecond;
            Enabled = enabled;
        }

        /// <summary>
        /// Parses a fire height string from the Leuchtfeuer registry, e.g.
        /// "59 m" or "13 m". Only the leading numeric part is read, so
        /// malformed units do not break the parse. Returns 0 when the string
        /// contains no number.
        /// </summary>
        public static float ParseFireHeight(string fireHeight)
        {
            if (string.IsNullOrWhiteSpace(fireHeight))
            {
                return 0f;
            }

            string text = fireHeight.Trim().Replace(',', '.');
            int end = 0;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == ','))
            {
                end++;
            }
            if (end == 0)
            {
                return 0f;
            }

            float value;
            if (float.TryParse(text.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return 0f;
        }
    }
}
