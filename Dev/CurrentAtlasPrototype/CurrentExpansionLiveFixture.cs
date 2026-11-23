using System;
using System.Globalization;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Dock;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    public static class CurrentExpansionLiveFixture
    {
        public static string Hud()
        {
            var hud = UnityEngine.Object.FindObjectOfType<CurrentRouteEtaHud>();
            var label = hud ? hud.GetComponentInChildren<Text>() : null;
            if (!label || !label.isActiveAndEnabled)
                return "";
            foreach (char c in label.text)
                if (!label.font || !label.font.HasCharacter(c))
                    return "missing glyph U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
            return label.text;
        }

        public static string LongInterval(int seconds)
        {
            return MeasureInterval(seconds, null);
        }

        public static string NativeVelocityReset()
        {
            foreach (var group in UnityEngine.Object.FindObjectsOfType<SandboxMobileGroup>())
            {
                if (group is SandboxPlayerWolfpack || group.SpawnState != SpawnState.NotSpawned
                    || group.IsAirborne || group.IsBeingDestroyed || group.Route == null || group.Route.Count == 0
                    || group.Route[0] == group.Position)
                    continue;
                var body = group.GetComponent<Rigidbody2D>();
                if (!body)
                    continue;
                group.Velocity = group.Velocity == .002f ? .003f : .002f;
                Vector2 residual = body.velocity - group.Up * group.Velocity;
                group.Route.Clear();
                return string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R},{3:R}",
                    residual.x, residual.y, body.velocity.x, body.velocity.y);
            }
            return "missing";
        }

        public static string AtlasInterval(double latitude, double longitude)
        {
            return MeasureInterval(600, Coordinates.ToWorldPosition((float)latitude, (float)longitude));
        }

        private static string MeasureInterval(int seconds, Vector2? placement)
        {
            var origin = typeof(SandboxMobileGroup).GetField("lastUpdateTime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var group in UnityEngine.Object.FindObjectsOfType<SandboxMobileGroup>())
            {
                if (group is SandboxPlayerWolfpack || group.SpawnState != SpawnState.NotSpawned
                    || group.IsAirborne || group.IsBeingDestroyed || group.LastUpdateTicks <= 0)
                    continue;
                var body = group.GetComponent<Rigidbody2D>();
                if (!body)
                    continue;
                group.Velocity = 0f;
                if (placement.HasValue)
                    group.Position = placement.Value;
                group.UpdateGroup();
                var before = group.Position;
                var velocityBefore = body.velocity;
                origin.SetValue(group, group.LastUpdateTicks - seconds * TimeSpan.TicksPerSecond);
                group.UpdateGroup();
                Vector2 drift = group.Position - before;
                return string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R}",
                    drift.x, drift.y, body.velocity.x, body.velocity.y, group.Velocity,
                    velocityBefore.x, velocityBefore.y);
            }
            return "missing";
        }

        public static string FairwayStart(bool offshore)
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!ship || !ship.Docked)
                return "missing";
            DockArea area = ship.CurrentDockArea;
            if (!area && ship.CurrentDock?.SpawnedEntity)
                area = DockArea.FindPreferredAutoDockArea(ship.CurrentDock.SpawnedEntity);
            if (!area)
                return "missing";
            Vector2 trigger = Sandbox.Instance.SceneToEncounterSpace(area.transform.position);
            Vector2 start = trigger + Vector2.left * 3f;
            var fairway = area.UndockRoute;
            if (fairway != null && fairway.Length >= 2)
            {
                if (offshore)
                {
                    Vector2 end = fairway[fairway.Length - 1];
                    Vector2 prior = fairway[fairway.Length - 2];
                    start = end + (prior - end).normalized * 0.10f;
                }
                else
                {
                    Vector2 first = fairway[1];
                    Vector2 toSea = first - trigger;
                    start = trigger + toSea.normalized * Mathf.Min(0.9f, toSea.magnitude * 0.7f);
                }
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R},{3:R},{4}",
                start.x, start.y, trigger.x, trigger.y, fairway == null ? 0 : fairway.Length);
        }

        public static string DockAreas()
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!ship)
                return "missing";
            var text = new System.Text.StringBuilder();
            var controller = InjectionFramework.Instance?.GetInstance<IDockAreasController>();
            var areas = controller?.DockAreas;
            text.Append("registered=").Append(areas?.Count ?? -1).Append(';');
            foreach (var area in UnityEngine.Resources.FindObjectsOfTypeAll<DockArea>())
            {
                if (!area || !area.gameObject.scene.IsValid())
                    continue;
                var port = area.DockLocator ? ParentEntityReference.GetParentEntity(area.DockLocator) : null;
                Vector2 pos = Sandbox.Instance.SceneToEncounterSpace(area.transform.position);
                Vector2 here = Sandbox.Instance.SceneToEncounterSpace(ship.transform.position);
                if ((pos - here).sqrMagnitude > 3600f)
                    continue;
                text.AppendFormat(CultureInfo.InvariantCulture,
                    "{0}|{1}|{2:R},{3:R}|active={4}|free={5}|ally={6}|points={7};", area.name,
                    port ? port.name : "missing", pos.x, pos.y, area.isActiveAndEnabled, area.IsFree,
                    port && port.Country != null && ship.Country.GetRelationWith(port.Country) == Country.Relation.Ally,
                    area.UndockRoute?.Length ?? 0);
            }
            return text.ToString();
        }

        public static bool Place(double x, double y)
        {
            var group = InjectionFramework.Instance?.GetInstance<SandboxPlayerWolfpack>();
            if (!group)
                return false;
            group.SetPosition(new Vector2((float)x, (float)y), true);
            return true;
        }

        public static string Sailing()
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!ship)
                return "missing";
            Vector2 point = Sandbox.Instance.SceneToEncounterSpace(ship.transform.position);
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3}",
                ship.Docked ? 1 : 0, point.x, point.y, ship.PrivateRoute.Count);
        }

        public static bool SetLeg(float x, float y)
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!ship || ship.Docked)
                return false;
            ship.PrivateRoute.Set(new Vector2(x, y));
            ship.PrivateRoute.IsPlayerOrder = true;
            return ship.PrivateRoute.Count == 1;
        }

        public static bool ClearLeg()
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!ship)
                return false;
            ship.PrivateRoute.Clear(false);
            ship.MobileSandboxGroup.Route.Clear(false);
            return ship.Route.Count == 0;
        }
    }
}
