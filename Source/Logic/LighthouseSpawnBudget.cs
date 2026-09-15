namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// 3D-slot accounting for the lighthouse placer (F222). A placement
    /// consumes a slot when it is spawned OR already granted
    /// (<c>WantsSpawn</c>) but not yet spawned. The original code only
    /// counted <c>Spawned</c>, so pending grants were invisible to the
    /// concurrency cap and it could be exceeded across ticks. Pure, unit-tested
    /// offline in LighthouseSpawnBudgetTests.
    /// </summary>
    public static class LighthouseSpawnBudget
    {
        /// <summary>1 when the placement occupies a 3D slot, else 0.</summary>
        public static int Consumed(bool spawned, bool wantsSpawn)
        {
            return (spawned || wantsSpawn) ? 1 : 0;
        }

        /// <summary>True while another placement may be granted.</summary>
        public static bool CanGrant(int consumed, int maxConcurrent)
        {
            return consumed < maxConcurrent;
        }
    }
}
