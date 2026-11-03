using System.Reflection;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>The game's mod compiler omits TOD_Sky's assembly. Read the same native values without a typed dependency.</summary>
    internal static class CelestialWorldReadback
    {
        internal static bool TryRead(object weather, out double latitude, out double longitude, out double utc)
        {
            latitude = longitude = utc = double.NaN;
            object sky = Member(weather, "Sky"), world = Member(sky, "World");
            return Number(Member(world, "Latitude"), out latitude)
                && Number(Member(world, "Longitude"), out longitude)
                && Number(Member(world, "UTC"), out utc);
        }

        private static object Member(object instance, string name)
        {
            if (instance == null) return null;
            MemberInfo member = TypeMemberCache.GetInstanceMember(instance.GetType(), name);
            var property = member as PropertyInfo;
            if (property != null) return property.GetValue(instance, null);
            var field = member as FieldInfo;
            return field != null ? field.GetValue(instance) : null;
        }

        private static bool Number(object value, out double result)
        {
            result = double.NaN;
            if (value is double) result = (double)value;
            else if (value is float) result = (float)value;
            else if (value is int) result = (int)value;
            else return false;
            return !double.IsNaN(result) && !double.IsInfinity(result);
        }
    }
}
