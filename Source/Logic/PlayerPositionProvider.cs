using System;
using System.Reflection;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Feeds the player submarine's world position into bearing calculations.
    // The game keeps the ship in the static field Entity.playerShip;
    // PlayerShip.SandboxPlayerShip.Position is a Vector2 in world units where
    // 1 unit = 1/111.3198888888889 degree (the exact factor mirrored by
    // WorldPosition.FromCoordinates).
    public interface IPlayerPositionProvider
    {
        bool TryGetPosition(out float latitude, out float longitude);
    }

    public class GamePlayerPositionProvider : IPlayerPositionProvider
    {
        private static FieldInfo playerShipField;
        private static PropertyInfo sandboxShipProperty;
        private static PropertyInfo positionProperty;

        public bool TryGetPosition(out float latitude, out float longitude)
        {
            latitude = 0f;
            longitude = 0f;

            try
            {
                object ship = ResolvePlayerShipField().GetValue(null);
                if (ship.IsNull())
                    return false;

                object sandbox = ResolveSandboxShipProperty(ship.GetType()).GetValue(ship, null);
                if (sandbox.IsNull())
                    return false;

                Vector2 position = (Vector2)ResolvePositionProperty(sandbox.GetType()).GetValue(sandbox, null);
                // SandboxGroup starts with an uninitialized (NaN, NaN) position
                // before the scene finishes placing the player ship. Do not let
                // that value mark the fix pipeline as available permanently.
                if (!IsFinite(position.x) || !IsFinite(position.y))
                    return false;

                GeoPositionConverter.ToLatLong(position.x, position.y, out latitude, out longitude);
                return IsFinite(latitude) && IsFinite(longitude);
            }
            catch
            {
                // No ship in scene (menu, port loading), member renamed by a game
                // update, or Unity API unavailable — the caller must not show a fix.
                return false;
            }
        }

        private static FieldInfo ResolvePlayerShipField()
        {
            if (playerShipField.IsNull())
                playerShipField = typeof(Entity).GetField("playerShip",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return playerShipField;
        }

        private static PropertyInfo ResolveSandboxShipProperty(Type shipType)
        {
            if (sandboxShipProperty.IsNull())
                sandboxShipProperty = shipType.GetProperty("SandboxPlayerShip",
                    BindingFlags.Public | BindingFlags.Instance);
            return sandboxShipProperty;
        }

        private static PropertyInfo ResolvePositionProperty(Type sandboxType)
        {
            if (positionProperty.IsNull())
                positionProperty = sandboxType.GetProperty("Position",
                    BindingFlags.Public | BindingFlags.Instance);
            return positionProperty;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
