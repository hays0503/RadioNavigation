using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    public sealed partial class SextantController
    {
        private readonly SextantLopMapSession lopMapSession = new SextantLopMapSession();
        private readonly SextantLopMapLines lopMapLines = new SextantLopMapLines();
        private float lopMapRetryTimer;

        private void BindLopMap()
        {
            worksheet.LopPlotChanged = PublishLopMap;
            worksheet.LopPlotRestored = lopMapSession.Restore;
            lopMapSession.Restore(); // also adopt saved native lines when no carrier is pending
        }

        private void PublishLopMap(SextantLopSegment[] lines)
        {
            lopMapSession.Publish(lines);
            FlushLopMap();
        }

        internal void FlushLopMap()
        {
            lopMapSession.Flush(lopMapLines, SextantSavePersistence.IsLoading);
        }

        private void UpdateLopMap(float dt)
        {
            if (SextantSavePersistence.IsLoading)
                return;
            lopMapRetryTimer -= dt;
            if (lopMapRetryTimer <= 0)
            {
                lopMapRetryTimer = 1;
                FlushLopMap();
            }
            lopMapLines.UpdateInteraction();
        }
    }
}
