namespace UBOAT.Mods.RadioNavigation
{
    public enum ChronometerCursorCommand
    {
        None,
        ArrowsExpand,
        ArrowsReduce,
        Crosshair,
        GameDefault
    }

    /// <summary>
    /// Pure emit-only-on-change decision stream for the chronometer cursor.
    /// Encodes the two-zone model: the scale hint owns the center (arrows,
    /// expand/reduce follows the toggled state), the rest of the body hints
    /// the move crosshair, everything else falls back to the game default.
    /// Identical inputs produce None so executors never spam Cursor.SetCursor.
    /// </summary>
    public sealed class ChronometerCursorPolicy
    {
        private bool initialized;
        private bool lastScaleHovered;
        private bool lastMoveHovered;
        private bool lastExpanded;

        public ChronometerCursorCommand Evaluate(bool scaleHovered, bool moveHovered, bool expanded)
        {
            bool effectiveMove = moveHovered && !scaleHovered; // arrows own the center

            ChronometerCursorCommand desired = scaleHovered
                ? (expanded ? ChronometerCursorCommand.ArrowsReduce : ChronometerCursorCommand.ArrowsExpand)
                : effectiveMove
                    ? ChronometerCursorCommand.Crosshair
                    : ChronometerCursorCommand.GameDefault;

            if (!initialized
                || lastScaleHovered != scaleHovered
                || lastMoveHovered != effectiveMove
                || lastExpanded != expanded)
            {
                initialized = true;
                lastScaleHovered = scaleHovered;
                lastMoveHovered = effectiveMove;
                lastExpanded = expanded;
                return desired;
            }

            return ChronometerCursorCommand.None;
        }
    }
}
