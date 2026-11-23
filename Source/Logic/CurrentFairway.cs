using System;
using System.Collections.Generic;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Dock;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI.DevConsole;
using UnityEngine;
using UnityEngine.AI;

namespace UBOAT.Mods.RadioNavigation
{
    // An explicit order, not an automatic route replacement or an auto-dock.
    internal static class CurrentFairway
    {
        private const string Prefix = "[RadioNav][CURRENT][FAIRWAY] ";
        private const float MaxApproachKm = 60f;
        private const float MaxEndErrorKm = 0.05f;
        private const float OnFairwayKm = 0.015f;
        private static PlayerShip lastSkipper;
        private static SandboxEntity lastPort;
        private static WorldPosition[] lastLane;
        private static DockArea lastBerth;
        private static SavesManager saves;

        internal static void RememberDepartureBerth(PlayerShip ship)
        {
            if (!saves)
            {
                saves = ScriptableObjectSingleton.LoadSingleton<SavesManager>();
                if (saves)
                    saves.LoadingInitialization += OnLoading;
            }
            if (ship != lastSkipper)
                ForgetDeparture(ship);
            if (!ship || !ship.Docked)
                return;
            var area = ship.CurrentDockArea;
            if (ReferenceEquals(area, lastBerth) && ship.CurrentDock == lastPort)
                return;
            lastBerth = area;
            lastPort = ship.CurrentDock;
            lastLane = null;
            if (!area || area.UndockRoute == null || area.UndockRoute.Length < 2)
                return;
            lastLane = (WorldPosition[])area.UndockRoute.Clone();
            lastLane[0] = new ScenePosition2D(area.transform.position).ToWorldPosition();
        }

        // A campaign load drops the berth memory once, at the load event, not
        // through IsLoading: that flag also covers ordinary port scene loads.
        private static void OnLoading()
        {
            ForgetDeparture(null);
        }

        private static void ForgetDeparture(PlayerShip ship)
        {
            lastSkipper = ship;
            lastPort = null;
            lastLane = null;
            lastBerth = null;
        }

        internal static void Register()
        {
            DevConsole.AddListener("rn_current_fairway", Order);
        }

        private static void Order(string[] args)
        {
            var injection = InjectionFramework.Instance;
            var ship = injection?.GetInstance<PlayerShip>();
            var nav = injection?.GetInstance<WorldNavMesh>();
            var sandbox = Sandbox.Instance;
            if (!ship || ship.Docked || !nav || sandbox == null)
            {
                Debug.LogWarning(Prefix + "approach requires a loaded campaign and a boat at sea");
                return;
            }

            Vector2 start = sandbox.SceneToEncounterSpace(ship.transform.position);
            string name = args == null || args.Length < 2 ? "" : string.Join(" ", args, 1, args.Length - 1);
            var waypoints = new List<Vector2>();
            var docks = injection.GetInstance<IDockAreasController>();
            DockArea area = FindRoute(ship, sandbox, nav, start, name, waypoints, docks?.DockAreas);
            if (area)
            {
                InstallRoute(ship, waypoints, area.name + DescribeRoute(area));
                return;
            }
            if (TryReturnToDeparture(nav, ship, start, name, waypoints))
            {
                InstallRoute(ship, waypoints, lastPort.Name + " (remembered authored fairway)");
                return;
            }
            Debug.LogWarning(Prefix + "no complete water route to a free allied dock within 60 km; route unchanged");
        }

        private static string DescribeRoute(DockArea area) =>
            area.UndockRoute?.Length >= 2 ? " (authored fairway)" : " (native water mesh)";

        private static void InstallRoute(PlayerShip ship, List<Vector2> waypoints, string description)
        {
            var route = ship.PrivateRoute;
            route.SetQuiet(waypoints[0]);
            for (int i = 1; i < waypoints.Count; i++)
                route.AddQuiet(waypoints[i]);
            route.IsPlayerOrder = true;
            Debug.Log(Prefix + "ordered " + waypoints.Count + " waypoints to " + description);
        }

        private static DockArea FindRoute(PlayerShip ship, Sandbox sandbox, WorldNavMesh nav,
            Vector2 start, string name, List<Vector2> route, IReadOnlyList<DockArea> areas)
        {
            if (areas == null)
                return null;
            DockArea best = null;
            float bestScore = float.PositiveInfinity;
            var candidate = new List<Vector2>();
            foreach (DockArea area in areas)
            {
                if (!CanUse(ship, area, name))
                    continue;
                float distance = (sandbox.SceneToEncounterSpace(area.transform.position) - start).sqrMagnitude;
                if (distance > MaxApproachKm * MaxApproachKm)
                    continue;
                var fairway = area.UndockRoute;
                bool onLane = fairway?.Length >= 2 && JoinIndex(fairway, start) >= 0;
                float score = distance - (onLane ? MaxApproachKm * MaxApproachKm : 0f);
                if (score >= bestScore)
                    continue;
                candidate.Clear();
                Vector2 trigger = sandbox.SceneToEncounterSpace(area.transform.position);
                if (!TryPlan(nav, fairway, trigger, start, candidate))
                    continue;
                bestScore = score;
                best = area;
                route.Clear();
                route.AddRange(candidate);
            }
            return best;
        }

        private static bool CanUse(PlayerShip ship, DockArea area, string name)
        {
            if (!area || !area.isActiveAndEnabled || !area.IsFree || !area.DockLocator)
                return false;
            var port = ParentEntityReference.GetParentEntity(area.DockLocator);
            if (!port || port.Country == null || ship.Country.GetRelationWith(port.Country) != Country.Relation.Ally
                || ship.Country.Affiliation == Affiliation.Neutral)
                return false;
            return name.Length == 0 || Matches(port.name, name) || Matches(port.SandboxEntity?.Name, name)
                || Matches(area.name, name);
        }

        private static bool Matches(string text, string name) =>
            text?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool TryReturnToDeparture(WorldNavMesh nav, PlayerShip ship, Vector2 start,
            string name, List<Vector2> route)
        {
            if (ship != lastSkipper || lastLane == null || lastPort?.Country == null
                || !lastPort.Enabled || ship.Country.Affiliation == Affiliation.Neutral
                || ship.Country.GetRelationWith(lastPort.Country) != Country.Relation.Ally
                || (name.Length != 0 && !Matches(lastPort.Name, name))
                || (lastBerth && lastBerth.gameObject.activeInHierarchy))
                return false;
            Vector2 trigger = lastLane[0];
            return (trigger - start).sqrMagnitude <= MaxApproachKm * MaxApproachKm
                && TryPlan(nav, lastLane, trigger, start, route);
        }

        private static bool TryPlan(WorldNavMesh nav, WorldPosition[] fairway, Vector2 trigger, Vector2 start,
            List<Vector2> waypoints)
        {
            bool authored = fairway?.Length >= 2;
            int laneIndex = authored ? JoinIndex(fairway, start) : -1;
            int join = authored ? fairway.Length - 1 : 0;
            if (laneIndex >= 0)
                join = laneIndex;
            Vector2 entry = join > 0 ? (Vector2)fairway[join] : trigger;
            if (laneIndex < 0 && !ConnectAtSea(nav, start, entry, waypoints))
                return false;
            if (join > 0 && waypoints.Count == 0 && (entry - start).sqrMagnitude > 0.000001f)
                waypoints.Add(entry);

            for (int i = join - 1; i >= 1; i--)
                waypoints.Add(fairway[i]);
            // The trigger, not the berth locator or port entity origin, owns docking.
            if (waypoints.Count == 0 || waypoints[waypoints.Count - 1] != trigger)
                waypoints.Add(trigger);
            return true;
        }

        private static bool ConnectAtSea(WorldNavMesh nav, Vector2 start, Vector2 entry, List<Vector2> waypoints)
        {
            if ((entry - start).sqrMagnitude <= MaxEndErrorKm * MaxEndErrorKm)
                return true;
            var status = nav.FindPath(start, entry, waypoints, true, false, false, false, true);
            return status == NavMeshPathStatus.PathComplete && waypoints.Count != 0
                && (waypoints[waypoints.Count - 1] - entry).sqrMagnitude <= MaxEndErrorKm * MaxEndErrorKm;
        }

        // A boat already in the authored lane joins it in the inbound direction;
        // an offshore approach still starts at the seaward end of the fairway.
        private static int JoinIndex(WorldPosition[] fairway, Vector2 start)
        {
            int join = -1;
            float best = OnFairwayKm * OnFairwayKm;
            for (int i = 1; i < fairway.Length; i++)
            {
                Vector2 inner = fairway[i - 1];
                Vector2 delta = (Vector2)fairway[i] - inner;
                if (delta.sqrMagnitude <= 0f)
                    continue;
                float along = Vector2.Dot(start - inner, delta) / delta.sqrMagnitude;
                if (along < 0f || along > 1f)
                    continue;
                float distance = (start - (inner + delta * along)).sqrMagnitude;
                if (distance >= best)
                    continue;
                best = distance;
                join = i - 1;
            }
            return join;
        }
    }
}
