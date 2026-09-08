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
                FieldInfo shipField = ResolvePlayerShipField();
                if (shipField.IsNull())
                    return false;
                object ship = shipField.GetValue(null);
                if (ship.IsNull())
                    return false;

                object sandbox = null;
                PropertyInfo sandboxProp = ResolveSandboxShipProperty(ship.GetType());
                if (sandboxProp.IsNotNull())
                    sandbox = sandboxProp.GetValue(ship, null);
                if (sandbox.IsNull())
                    return false;

                Vector2 position = Vector2.zero;
                PropertyInfo positionProp = ResolvePositionProperty(sandbox.GetType());
                if (positionProp.IsNull())
                    return false;
                position = (Vector2)positionProp.GetValue(sandbox, null);
                // SandboxGroup starts with an uninitialized (NaN, NaN) position
                // before the scene finishes placing the player ship. Do not let
                // that value mark the fix pipeline as available permanently.
                if (!IsFinite(position.x) || !IsFinite(position.y))
                    return false;

                GeoPositionConverter.ToLatLong(position.x, position.y, out latitude, out longitude);
                return IsFinite(latitude) && IsFinite(longitude);
            }
            catch (Exception e)
            {
                // No ship in scene (menu, port loading) is expected; a game
                // update renaming members is not — log once per session so
                // the two are distinguishable in Player.log.
                if (WarnOnce.ShouldLog("PlayerPositionProvider.TryGetPosition"))
                    Debug.LogWarning("[RadioNav][POSITION] player position unavailable: " + e.Message);
                return false;
            }
        }

        private static FieldInfo ResolvePlayerShipField()
        {
            if (playerShipField.IsNull())
            {
                playerShipField = typeof(Entity).GetField("playerShip",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (playerShipField.IsNull() && WarnOnce.ShouldLog("PlayerPositionProvider.playerShip"))
                    Debug.LogWarning("[RadioNav][POSITION] Entity.playerShip field not found — game renamed it or DI not ready");
            }
            return playerShipField;
        }

        private static PropertyInfo ResolveSandboxShipProperty(Type shipType)
        {
            if (sandboxShipProperty.IsNull())
            {
                sandboxShipProperty = shipType.GetProperty("SandboxPlayerShip",
                    BindingFlags.Public | BindingFlags.Instance);
                if (sandboxShipProperty.IsNull() && WarnOnce.ShouldLog("PlayerPositionProvider.SandboxPlayerShip"))
                    Debug.LogWarning("[RadioNav][POSITION] SandboxPlayerShip property not found on " + shipType.Name);
            }
            return sandboxShipProperty;
        }

        private static PropertyInfo ResolvePositionProperty(Type sandboxType)
        {
            if (positionProperty.IsNull())
            {
                positionProperty = sandboxType.GetProperty("Position",
                    BindingFlags.Public | BindingFlags.Instance);
                if (positionProperty.IsNull() && WarnOnce.ShouldLog("PlayerPositionProvider.Position"))
                    Debug.LogWarning("[RadioNav][POSITION] Position property not found on " + sandboxType.Name);
            }
            return positionProperty;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
