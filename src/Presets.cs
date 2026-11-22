using System;
using System.Collections.Generic;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    internal enum MoveKind
    {
        Straight,     // offset in the view's frame (Dutch adds a roll)
        Arc,          // horizontal arc around the subject
        Orbit,        // full horizontal circle, looping
        VerticalArc,  // over or under the subject
        Spiral,       // full circle while rising or sinking
        Sway,         // looping side-to-side arc
        Hold,         // stays put (for Handheld and Dutch on their own)
    }

    /// <summary>One preset camera move.</summary>
    internal sealed class Preset
    {
        public string Id;
        public string Name;
        public string Hint;
        public MoveKind Kind;

        /// <summary>Straight moves: direction in view space (x = right, y = world up, z = forward along the view).</summary>
        public Vector3 Direction;

        /// <summary>Straight moves: y is the camera's own up (tilts with the shot) instead of world up.</summary>
        public bool ViewUp;

        /// <summary>Straight moves and Spiral: default size as a fraction of the distance to the subject.</summary>
        public float DefaultSize;

        /// <summary>Arcs and spirals: +1 = left / up, -1 = right / down.</summary>
        public float ArcSign;

        /// <summary>Dutch: +1 = roll left (counter-clockwise), -1 = roll right. Scaled by the Dutch angle setting.</summary>
        public float RollSign;

        /// <summary>Moves the camera must keep aiming on even with Track off (it would otherwise lose the subject).</summary>
        public bool AlwaysAims
        {
            get { return Kind == MoveKind.Arc || Kind == MoveKind.Orbit || Kind == MoveKind.VerticalArc || Kind == MoveKind.Spiral || Kind == MoveKind.Sway; }
        }

        // Laid out three per row in the window.
        public static readonly Preset[] All =
        {
            new Preset { Id = "push_in", Name = "Push In", Hint = "Move toward the subject along the view.", Kind = MoveKind.Straight, Direction = new Vector3(0, 0, 1), DefaultSize = 0.4f },
            new Preset { Id = "pull_out", Name = "Pull Out", Hint = "Move back from the subject along the view.", Kind = MoveKind.Straight, Direction = new Vector3(0, 0, -1), DefaultSize = 0.6f },
            new Preset { Id = "reveal", Name = "Reveal", Hint = "Pull back while rising.", Kind = MoveKind.Straight, Direction = new Vector3(0, 0.5f, -1f), DefaultSize = 0.6f },

            new Preset { Id = "crane_up", Name = "Crane Up", Hint = "Rise straight up.", Kind = MoveKind.Straight, Direction = new Vector3(0, 1, 0), DefaultSize = 0.4f },
            new Preset { Id = "crane_down", Name = "Crane Down", Hint = "Sink straight down.", Kind = MoveKind.Straight, Direction = new Vector3(0, -1, 0), DefaultSize = 0.4f },
            new Preset { Id = "hold", Name = "Hold", Hint = "Stay put for the duration. Pair with Handheld for a held-camera feel.", Kind = MoveKind.Hold },

            new Preset { Id = "trolley_left", Name = "Trolley Left", Hint = "Slide sideways to the left.", Kind = MoveKind.Straight, Direction = new Vector3(-1, 0, 0), DefaultSize = 0.5f },
            new Preset { Id = "trolley_right", Name = "Trolley Right", Hint = "Slide sideways to the right.", Kind = MoveKind.Straight, Direction = new Vector3(1, 0, 0), DefaultSize = 0.5f },
            new Preset { Id = "sway", Name = "Sway", Hint = "Gentle looping swing left and right around the subject (± half the Arc angle).", Kind = MoveKind.Sway },

            new Preset { Id = "trolley_up", Name = "Trolley Up", Hint = "Slide up along the camera's own up (tilts with the shot; Crane Up is straight up).", Kind = MoveKind.Straight, Direction = new Vector3(0, 1, 0), DefaultSize = 0.4f, ViewUp = true },
            new Preset { Id = "trolley_down", Name = "Trolley Down", Hint = "Slide down along the camera's own up (tilts with the shot; Crane Down is straight down).", Kind = MoveKind.Straight, Direction = new Vector3(0, -1, 0), DefaultSize = 0.4f, ViewUp = true },
            new Preset { Id = "orbit", Name = "Orbit 360", Hint = "Full circle to the left around the subject, looping.", Kind = MoveKind.Orbit, ArcSign = 1f },
            new Preset { Id = "orbit_right", Name = "Orbit 360 Right", Hint = "Full circle to the right around the subject, looping.", Kind = MoveKind.Orbit, ArcSign = -1f },

            new Preset { Id = "arc_left", Name = "Arc Left", Hint = "Circle left around the subject by the Arc angle.", Kind = MoveKind.Arc, ArcSign = 1f },
            new Preset { Id = "arc_right", Name = "Arc Right", Hint = "Circle right around the subject by the Arc angle.", Kind = MoveKind.Arc, ArcSign = -1f },
            new Preset { Id = "spiral_up", Name = "Spiral Up", Hint = "One full circle around the subject while rising.", Kind = MoveKind.Spiral, ArcSign = 1f, DefaultSize = 0.5f },

            new Preset { Id = "arc_up", Name = "Arc Up", Hint = "Swing up over the subject by the Arc angle (stops short of straight above).", Kind = MoveKind.VerticalArc, ArcSign = 1f },
            new Preset { Id = "arc_down", Name = "Arc Down", Hint = "Swing down under the subject by the Arc angle (stops short of straight below).", Kind = MoveKind.VerticalArc, ArcSign = -1f },
            new Preset { Id = "spiral_down", Name = "Spiral Down", Hint = "One full circle around the subject while sinking.", Kind = MoveKind.Spiral, ArcSign = -1f, DefaultSize = 0.5f },

            new Preset { Id = "dutch_left", Name = "Dutch Left", Hint = "Slow push in while rolling left by the Dutch angle.", Kind = MoveKind.Straight, Direction = new Vector3(0, 0, 1), DefaultSize = 0.15f, RollSign = 1f },
            new Preset { Id = "dutch_right", Name = "Dutch Right", Hint = "Slow push in while rolling right by the Dutch angle.", Kind = MoveKind.Straight, Direction = new Vector3(0, 0, 1), DefaultSize = 0.15f, RollSign = -1f },
        };
    }

    /// <summary>The window's two-step picker: a type, then a direction (each direction is a preset).</summary>
    internal sealed class MoveGroup
    {
        public string Name;
        public string[] Directions;
        public string[] PresetIds;

        public static readonly MoveGroup[] All =
        {
            new MoveGroup { Name = "Push", Directions = new[] { "In", "Out" }, PresetIds = new[] { "push_in", "pull_out" } },
            new MoveGroup { Name = "Trolley", Directions = new[] { "Left", "Right", "Up", "Down" }, PresetIds = new[] { "trolley_left", "trolley_right", "trolley_up", "trolley_down" } },
            new MoveGroup { Name = "Crane", Directions = new[] { "Up", "Down" }, PresetIds = new[] { "crane_up", "crane_down" } },
            new MoveGroup { Name = "Arc", Directions = new[] { "Left", "Right", "Up", "Down" }, PresetIds = new[] { "arc_left", "arc_right", "arc_up", "arc_down" } },
            new MoveGroup { Name = "Orbit", Directions = new[] { "Left", "Right" }, PresetIds = new[] { "orbit", "orbit_right" } },
            new MoveGroup { Name = "Spiral", Directions = new[] { "Up", "Down" }, PresetIds = new[] { "spiral_up", "spiral_down" } },
            new MoveGroup { Name = "Dutch", Directions = new[] { "Left", "Right" }, PresetIds = new[] { "dutch_left", "dutch_right" } },
            new MoveGroup { Name = "Reveal", Directions = new[] { "Back & up" }, PresetIds = new[] { "reveal" } },
            new MoveGroup { Name = "Sway", Directions = new[] { "Side to side" }, PresetIds = new[] { "sway" } },
            new MoveGroup { Name = "Hold", Directions = new[] { "Stay put" }, PresetIds = new[] { "hold" } },
        };

        public static Preset PresetById(string id)
        {
            foreach (var p in Preset.All) if (p.Id == id) return p;
            return null;
        }

        /// <summary>The group a preset belongs to, and its direction index in it.</summary>
        public static int Find(string presetId, out int direction)
        {
            for (int g = 0; g < All.Length; g++)
                for (int d = 0; d < All[g].PresetIds.Length; d++)
                    if (All[g].PresetIds[d] == presetId) { direction = d; return g; }
            direction = -1;
            return -1;
        }
    }

    /// <summary>Where a move starts from and which way is "forward", "right" and "up" for it.</summary>
    internal struct ViewFrame
    {
        public Vector3 Origin;        // where the target is now
        public Vector3 ViewPosition;  // where the view looks from (the camera itself, or the viewport)
        public Vector3 Forward;       // view direction (with pitch)
        public Vector3 FlatForward;   // view direction on the ground plane
        public Vector3 Right;         // view right on the ground plane
        public Vector3 Up;            // the view's own up (tilted with its pitch)
        public Vector3 Subject;       // point the camera is looking at
        public float SubjectDistance; // distance from the view to the subject
    }

    internal sealed class Waypoint
    {
        public Vector3 World;
        public Vector3? AidWorld; // curve control point for the segment that starts here
        public StudioTween.EaseType Ease = StudioTween.EaseType.linear;
        public float Weight = 1f; // time this segment takes relative to a linear one of the same length and peak speed
        public float Speed;       // Studio's route speed value (it moves at speed x 10 units per second)
    }

    internal sealed class RoutePlan
    {
        public Vector3 Origin;
        public Quaternion Rotation;
        public OIRouteInfo.Orient Orient;
        public bool Loop;
        public readonly List<Waypoint> Points = new List<Waypoint>();
    }

    internal static class Planner
    {
        // Points are added directly, so Studio's optional 10-point limit (which only blocks the Add Point
        // button) doesn't apply. More points = rounder curves.
        // A Studio curve segment gets one control point, so its spline bulges and flattens between points. At 15° a
        // step the radius stays within a fraction of a percent, so circles keep one distance to the subject.
        private const int MaxPoints = 32;
        private const float MaxArcStep = 15f;
        private const int CircleSegments = 24;
        private const float MaxElevation = 80f; // vertical arcs stop short of straight up/down

        public struct Options
        {
            public float SizeScale;      // 1 = preset default
            public bool FixedDistance;   // size in Studio units instead of a share of the subject distance
            public float FixedBase;      // units for size 100% in fixed mode
            public float ArcAngle;       // degrees
            public float Duration;       // seconds
            public EaseCurve Curve;
            public EaseMode Mode;
            public bool Loop;
        }

        public static RoutePlan Build(Preset preset, ViewFrame view, Options opt)
        {
            switch (preset.Kind)
            {
                // Arcs don't loop: Studio would return along a straight line back to the start.
                case MoveKind.Arc: return BuildHorizontalArc(view, opt, preset.ArcSign, Mathf.Clamp(opt.ArcAngle, 1f, 359f), false);
                case MoveKind.Orbit: return BuildHorizontalArc(view, opt, preset.ArcSign, 360f, true);
                case MoveKind.VerticalArc: return BuildVerticalArc(view, opt, preset.ArcSign);
                case MoveKind.Spiral: return BuildSpiral(preset, view, opt);
                case MoveKind.Sway: return BuildSway(view, opt);
                case MoveKind.Hold: return BuildHold(view, opt);
                default: return BuildStraight(preset, view, opt);
            }
        }

        private static float Length(Preset preset, ViewFrame view, Options opt)
        {
            float baseLen = opt.FixedDistance ? opt.FixedBase : view.SubjectDistance * preset.DefaultSize;
            return Mathf.Max(0.01f, baseLen * opt.SizeScale);
        }

        private static RoutePlan NewPlan(ViewFrame view, bool loop)
        {
            return new RoutePlan
            {
                Origin = view.Origin,
                Rotation = Quaternion.LookRotation(view.FlatForward, Vector3.up),
                // Correct Aim stays None: Studio's own aiming turns in steps at each point. Tracker aims every
                // frame instead (and handles roll and handheld shake).
                Orient = OIRouteInfo.Orient.None,
                Loop = loop,
            };
        }

        // ---------------------------------------------------------------- straight, hold

        private static RoutePlan BuildStraight(Preset preset, ViewFrame view, Options opt)
        {
            Vector3 up = preset.ViewUp ? view.Up : Vector3.up;
            Vector3 dir = view.Right * preset.Direction.x + up * preset.Direction.y + view.Forward * preset.Direction.z;
            if (dir.sqrMagnitude < 1e-6f) dir = view.Forward;
            dir.Normalize();

            var plan = NewPlan(view, opt.Loop);
            // One segment carrying the chosen ease. Studio's tween engine applies it over the whole move.
            plan.Points.Add(new Waypoint { World = view.Origin, Ease = Easing.ToStudio(opt.Curve, opt.Mode) });
            plan.Points.Add(new Waypoint { World = view.Origin + dir * Length(preset, view, opt) });
            AssignSpeeds(plan, opt.Duration);
            return plan;
        }

        private static RoutePlan BuildHold(ViewFrame view, Options opt)
        {
            // A route has to travel to take time, so it creeps 0.01 units (a tenth of a millimetre) forward.
            var plan = NewPlan(view, opt.Loop);
            plan.Points.Add(new Waypoint { World = view.Origin });
            plan.Points.Add(new Waypoint { World = view.Origin + view.FlatForward * 0.01f });
            AssignSpeeds(plan, opt.Duration);
            return plan;
        }

        // ---------------------------------------------------------------- curved paths

        /// <summary>
        /// Samples a curved path into Studio route points: one point every segment, each with its curve control
        /// point (aid) at the segment's middle. <paramref name="at"/> maps 0..1 along the path to a position.
        /// A closed path (loop) has no point at 1: the loop's last segment returns to the start.
        /// </summary>
        private static RoutePlan Sample(ViewFrame view, Func<float, Vector3> at, int segments, bool closed)
        {
            var plan = NewPlan(view, closed);
            int pointCount = closed ? segments : segments + 1;
            for (int k = 0; k < pointCount; k++)
            {
                var wp = new Waypoint { World = at((float)k / segments) };
                if (k < segments) wp.AidWorld = at((k + 0.5f) / segments);
                plan.Points.Add(wp);
            }
            return plan;
        }

        /// <summary>
        /// Easing over several segments: the first eases in, the last eases out, the ones between run at the
        /// speed the ease hands over at. A single segment carries the whole ease. Loops stay at constant speed.
        /// </summary>
        private static void EaseEnds(RoutePlan plan, int segments, Options opt)
        {
            if (plan.Loop || opt.Curve == EaseCurve.Linear) return;
            if (segments == 1)
            {
                plan.Points[0].Ease = Easing.ToStudio(opt.Curve, opt.Mode);
                return;
            }
            float w = Easing.JoinSpeed(opt.Curve);
            if (opt.Mode != EaseMode.Out)
            {
                plan.Points[0].Ease = Easing.ToStudio(opt.Curve, EaseMode.In);
                plan.Points[0].Weight = w;
            }
            if (opt.Mode != EaseMode.In)
            {
                plan.Points[segments - 1].Ease = Easing.ToStudio(opt.Curve, EaseMode.Out);
                plan.Points[segments - 1].Weight = w;
            }
        }

        /// <summary>Centre to circle around and the camera's offset from it, with a usable horizontal radius.</summary>
        private static void Circle(ViewFrame view, out Vector3 center, out Vector3 offset)
        {
            center = view.Subject;
            offset = view.Origin - center;
            if (new Vector3(offset.x, 0f, offset.z).sqrMagnitude < 0.0001f)
            {
                // Camera right above the subject: put the pivot out in front so there is a radius.
                center = view.Origin + view.FlatForward * Mathf.Max(1f, view.SubjectDistance);
                offset = view.Origin - center;
            }
        }

        private static RoutePlan BuildHorizontalArc(ViewFrame view, Options opt, float sign, float angle, bool loop)
        {
            Vector3 center, offset;
            Circle(view, out center, out offset);

            bool full = angle >= 359.9f;
            int segments = full ? CircleSegments : Mathf.Clamp(Mathf.CeilToInt(angle / MaxArcStep), 1, MaxPoints - 1);
            float total = sign * angle;
            var plan = Sample(view, u => center + Quaternion.Euler(0f, total * u, 0f) * offset, segments, full && loop);
            EaseEnds(plan, segments, opt);
            AssignSpeeds(plan, opt.Duration);
            return plan;
        }

        private static RoutePlan BuildVerticalArc(ViewFrame view, Options opt, float sign)
        {
            Vector3 center, offset;
            Circle(view, out center, out offset);

            // Rotating about the view's right axis by a positive angle lifts the camera over the subject.
            Vector3 axis = view.Right;
            float elevation = Mathf.Asin(Mathf.Clamp(offset.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            float limit = sign > 0f ? MaxElevation - elevation : elevation + MaxElevation;
            float angle = Mathf.Clamp(opt.ArcAngle, 1f, Mathf.Max(1f, limit));

            int segments = Mathf.Clamp(Mathf.CeilToInt(angle / MaxArcStep), 1, MaxPoints - 1);
            float total = sign * angle;
            var plan = Sample(view, u => center + Quaternion.AngleAxis(total * u, axis) * offset, segments, false);
            EaseEnds(plan, segments, opt);
            AssignSpeeds(plan, opt.Duration);
            return plan;
        }

        private static RoutePlan BuildSpiral(Preset preset, ViewFrame view, Options opt)
        {
            Vector3 center, offset;
            Circle(view, out center, out offset);

            float rise = preset.ArcSign * Length(preset, view, opt);
            var plan = Sample(view, u => center + Quaternion.Euler(0f, 360f * u, 0f) * offset + Vector3.up * (rise * u), CircleSegments, false);
            EaseEnds(plan, CircleSegments, opt);
            AssignSpeeds(plan, opt.Duration);
            return plan;
        }

        private static RoutePlan BuildSway(ViewFrame view, Options opt)
        {
            Vector3 center, offset;
            Circle(view, out center, out offset);

            // Centre → left → centre → right → (loop) centre. Each swing slows into its far end and speeds back
            // up through the middle; the speeds match at every join, so it reads as one smooth pendulum.
            float half = Mathf.Clamp(opt.ArcAngle * 0.5f, 1f, 90f);
            float[] at = { 0f, half, 0f, -half };
            var plan = NewPlan(view, true);
            EaseCurve curve = opt.Curve == EaseCurve.Linear ? EaseCurve.Sine : opt.Curve;
            for (int k = 0; k < 4; k++)
            {
                float a0 = at[k], a1 = at[(k + 1) % 4];
                bool outward = k % 2 == 0; // centre → side: ease out (slow at the far end)
                plan.Points.Add(new Waypoint
                {
                    World = center + Quaternion.Euler(0f, a0, 0f) * offset,
                    AidWorld = center + Quaternion.Euler(0f, (a0 + a1) * 0.5f, 0f) * offset,
                    Ease = Easing.ToStudio(curve, outward ? EaseMode.Out : EaseMode.In),
                    Weight = Easing.JoinSpeed(curve),
                });
            }
            AssignSpeeds(plan, opt.Duration);
            return plan;
        }

        /// <summary>
        /// Sets each segment's speed so the whole route takes <paramref name="duration"/> seconds and the speed
        /// doesn't jump where an eased segment meets a linear one. An eased segment covers its length at 1/w of
        /// the speed it hands over at (w = 2 for quad), so it gets w times the time of a linear segment of the
        /// same length.
        /// </summary>
        private static void AssignSpeeds(RoutePlan plan, float duration)
        {
            int n = plan.Points.Count;
            int segs = plan.Loop ? n : n - 1;
            if (segs <= 0) return;

            var lengths = new float[segs];
            var weights = new float[segs];
            float weighted = 0f;
            for (int i = 0; i < segs; i++)
            {
                var a = plan.Points[i];
                var b = plan.Points[(i + 1) % n];
                // Measured exactly as Studio's tween engine will (iTween's spline length), so Duration is exact.
                lengths[i] = a.AidWorld.HasValue
                    ? RoutePath.Length(new[] { a.World, a.AidWorld.Value, b.World })
                    : RoutePath.Length(new[] { a.World, b.World });
                weights[i] = a.Weight;
                weighted += weights[i] * lengths[i];
            }

            float peak = weighted / Mathf.Max(0.05f, duration); // units per second at full speed
            for (int i = 0; i < segs; i++)
                plan.Points[i].Speed = Mathf.Max(0.0001f, peak / weights[i] / 10f);

            if (!plan.Loop) plan.Points[n - 1].Speed = plan.Points[n - 2].Speed;
        }
    }
}
