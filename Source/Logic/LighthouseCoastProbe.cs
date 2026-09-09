using System;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Thin wrapper over the game's global CoastQuadTree (DI singleton,
    /// background-loaded from StreamingAssets/Maps/Coast Quad Tree.dat).
    /// GetDistanceToCoast answers "which side of the coast is this point
    /// on" for ANY world position without terrain streaming: the returned
    /// distance is signed in km — negative = land, positive = water.
    ///
    /// Ready stays false until the background load finishes; callers
    /// (rn_lighthouse_landprobe sweep) treat that as "unknown, retry".
    /// </summary>
    public static class LighthouseCoastProbe
    {
        [Inject] private static CoastQuadTree coastQuadTree = null;

        /// <summary>True when the coast data is loaded and queryable.</summary>
        public static bool Ready
        {
            // CoastQuadTree is a plain class (BackgroundTaskBase), not a
            // Unity object, so the conditional access is fake-null safe.
            get { return coastQuadTree?.IsLoading == false; }
        }

        /// <summary>
        /// Signed distance to the game's coast at the given geodetic point,
        /// in km (negative = land, positive = water). Returns false when
        /// the coast data is not loaded yet or the query failed.
        /// </summary>
        public static bool TryGetDistanceKm(double latitude, double longitude, out double distanceKm)
        {
            distanceKm = double.NaN;
            if (!Ready)
            {
                return false;
            }

            try
            {
                // Same conversion the game uses for its own spawn checks
                // (SandboxEntity passes SceneToEncounterSpace km straight in).
                Vector2 encounterKm = Coordinates.ToWorldPosition((float)latitude, (float)longitude);
                float signed = coastQuadTree.GetDistanceToCoast(encounterKm);
                if (float.IsNaN(signed) || float.IsInfinity(signed))
                {
                    return false;
                }

                distanceKm = signed;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[RadioNav][LandProbe] coast query failed: " + e.Message);
                return false;
            }
        }
    }
}
