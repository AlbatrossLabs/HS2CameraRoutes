using System.Collections.Generic;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    /// <summary>A Camera Routes route as it stood, so it can be rebuilt exactly (including later hand edits).</summary>
    internal sealed class RouteSnapshot
    {
        public RoutePlan Plan;
        public string Name;
        public RouteRecord Record;
        public bool ShowLine;
        public Color Color;

        public static RouteSnapshot Take(OCIRoute route)
        {
            var info = route.routeInfo;
            var plan = new RoutePlan
            {
                Origin = route.objectItem.transform.position,
                Rotation = route.objectItem.transform.rotation,
                Orient = info.orient,
                Loop = info.loop,
            };
            foreach (OCIRoutePoint p in route.listPoint)
            {
                Transform t = p.objectItem.transform;
                var wp = new Waypoint
                {
                    World = t.position,
                    Ease = p.routePointInfo.easeType,
                    Speed = p.routePointInfo.speed,
                };
                if (p.routePointInfo.connection == OIRoutePointInfo.Connection.Curve)
                    wp.AidWorld = t.TransformPoint(p.routePointInfo.aidInfo.changeAmount.pos);
                plan.Points.Add(wp);
            }

            var rec = RouteRegistry.Get(route);
            return new RouteSnapshot
            {
                Plan = plan,
                Name = route.name,
                Record = rec != null ? rec.Clone() : null,
                ShowLine = info.visibleLine,
                Color = info.color,
            };
        }
    }

    internal sealed class UndoEntry
    {
        public string Label;
        public int RouteKey = -1;          // route this action created (deleted on undo)
        public int CreatedCameraKey = -1;  // camera this action created (deleted on undo)
        public int TargetKey = -1;
        public int PrevParentKey = -1;     // where the target hung before (when no route was replaced)
        public Vector3 PrevPos;
        public Quaternion PrevRot = Quaternion.identity;
        public RouteSnapshot Replaced;     // route this action removed (rebuilt on undo)
        public bool Adjust;                // a live settings change; later ones on the same route merge into it
        public System.Action Custom;       // an action outside the scene's objects (Send to Timeline): undone by this alone
    }

    /// <summary>Undo for Camera Routes actions. Cleared when a scene is loaded or started fresh.</summary>
    internal static class UndoStack
    {
        private const int Max = 30;
        private static readonly List<UndoEntry> Entries = new List<UndoEntry>();

        public static int Count { get { return Entries.Count; } }
        public static string NextLabel { get { return Entries.Count > 0 ? Entries[Entries.Count - 1].Label : ""; } }

        public static void Push(UndoEntry e)
        {
            Entries.Add(e);
            if (Entries.Count > Max) Entries.RemoveAt(0);
        }

        public static void Clear() { Entries.Clear(); }

        public static UndoEntry Peek() { return Entries.Count > 0 ? Entries[Entries.Count - 1] : null; }

        public static UndoEntry Pop()
        {
            if (Entries.Count == 0) return null;
            var e = Entries[Entries.Count - 1];
            Entries.RemoveAt(Entries.Count - 1);
            return e;
        }
    }
}
