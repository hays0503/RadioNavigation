using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Input;
using UBOAT.Game.UI;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Cursor application backend for the chronometer zones (P5.3 CH-1):
    /// translates the pure ChronometerCursorPolicy command into the
    /// available backend — the game's CursorManager (etalon native path),
    /// the PanelDragHandler override, or the raw Unity Cursor — and owns
    /// the procedural expand/reduce arrow textures. Extracted from
    /// ChronometerOverlayHandler (SRP: cursor plumbing is not the overlay
    /// orchestrator's logic). GameDefault always syncs the manager's mode
    /// AND forces the texture: foreign UnsetCustomCursor calls can leave
    /// the mode at Default while our custom texture is still on screen,
    /// and the guarded SetDefaultCursor would early-out and keep it there.
    /// </summary>
    internal sealed class ChronometerCursorBackend
    {
        private static readonly Vector2 ScaleCursorHotspot = new Vector2(16f, 16f);

        private readonly ChronometerCursorPolicy policy = new ChronometerCursorPolicy();
        private Texture2D expandCursor;
        private Texture2D reduceCursor;

        /// <summary>
        /// Evaluates the hover/expanded state and applies the resulting
        /// cursor command. Single application point — the pure policy
        /// decides, this translates to the available backend.
        /// </summary>
        public void Apply(bool scaleHover, bool moveHover, bool expanded,
            CursorManager cursorManager, PanelDragHandler panelDragHandler)
        {
            var cmd = policy.Evaluate(scaleHover, moveHover, expanded);
            switch (cmd)
            {
                case ChronometerCursorCommand.ArrowsExpand:
                case ChronometerCursorCommand.ArrowsReduce:
                    EnsureScaleCursors();
                    Texture2D arrows = cmd == ChronometerCursorCommand.ArrowsReduce
                        ? reduceCursor
                        : expandCursor;
                    if (cursorManager.IsNotNull())
                        cursorManager.SetCustomCursor(arrows, ScaleCursorHotspot);
                    else if (panelDragHandler.IsNotNull())
                        panelDragHandler.SetCursorOverride(arrows, ScaleCursorHotspot);
                    else
                        Cursor.SetCursor(arrows, ScaleCursorHotspot, CursorMode.Auto);
                    break;

                case ChronometerCursorCommand.Crosshair:
                    Texture2D moveTexture = panelDragHandler.IsNotNull()
                        ? panelDragHandler.MoveCursorTexture
                        : null;
                    Vector2 moveHotspot = panelDragHandler.IsNotNull()
                        ? panelDragHandler.MoveCursorHotspot
                        : new Vector2(16f, 16f);
                    if (moveTexture.IsNull())
                        break; // no crosshair source - keep current cursor
                    if (cursorManager.IsNotNull())
                        cursorManager.SetCustomCursor(moveTexture, moveHotspot);
                    else if (panelDragHandler.IsNotNull())
                        panelDragHandler.SetCursorOverride(moveTexture, moveHotspot);
                    else
                        Cursor.SetCursor(moveTexture, moveHotspot, CursorMode.Auto);
                    break;

                case ChronometerCursorCommand.GameDefault:
                    if (cursorManager.IsNotNull())
                        ApplyGameDefaultCursor(cursorManager);
                    else if (panelDragHandler.IsNotNull())
                        panelDragHandler.ClearCursorOverride();
                    else
                        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                    break;
            }
        }

        /// <summary>Destroys the procedural arrow textures (teardown).</summary>
        public void Destroy()
        {
            if (expandCursor.IsNotNull())
                Object.Destroy(expandCursor);
            if (reduceCursor.IsNotNull())
                Object.Destroy(reduceCursor);
            expandCursor = null;
            reduceCursor = null;
        }

        // Sync the manager's mode to Default AND force the texture.
        private static void ApplyGameDefaultCursor(CursorManager cursorManager)
        {
            cursorManager.SetDefaultCursor();
            Texture2D normal = ShipControlUIBridge.GetGameDefaultCursor(cursorManager);
            if (normal.IsNotNull())
                Cursor.SetCursor(normal, Vector2.zero, CursorMode.Auto);
        }

        private void EnsureScaleCursors()
        {
            if (expandCursor.IsNotNull() && reduceCursor.IsNotNull())
                return;

            expandCursor = ChronometerCursorArt.CreateScaleCursor(false);
            reduceCursor = ChronometerCursorArt.CreateScaleCursor(true);
        }
    }
}
