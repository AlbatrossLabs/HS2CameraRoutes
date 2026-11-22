using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    public enum SubjectKind
    {
        Off,       // no aiming: straight moves keep their aim (arcs still face the pivot)
        Pivot,     // a fixed point: what the camera was looking at when the route was made
        Character, // a character's bone, followed as it moves
    }

    public enum SubjectBone
    {
        Head,
        Chest,
        Hips,
    }

    /// <summary>Everything Camera Routes knows about one of its routes. Saved with the scene.</summary>
    internal sealed class RouteRecord
    {
        private const int FormatVersion = 4;

        public int RouteKey = -1;
        public string PresetId = "";
        public SubjectKind Subject = SubjectKind.Pivot;
        public Vector3 Pivot;
        public int CharKey = -1;
        public string CharName = "";
        public SubjectBone Bone = SubjectBone.Head;
        public Quaternion Offset = Quaternion.identity; // camera aim relative to "looking straight at the subject"
        public float ArcSign;
        public Vector3 Origin;                           // where the route was placed; matches routes after an import
        public int ParentKey = -1;                       // what the target hung under before its first preset

        // The settings it was made with, for "Load settings".
        public float Size = 1f, ArcAngle = 45f, Duration = 4f;
        public EaseCurve Curve = EaseCurve.Quad;
        public EaseMode Mode = EaseMode.InOut;
        public bool Loop;
        public float Loops;                              // > 0: Duration was set as this many animation loops
        public float Roll;                               // degrees of roll by the end (Dutch), + = left
        public float Shake;                              // handheld amount, 0 = off
        public Quaternion StartRot = Quaternion.identity; // the target's rotation when the route was made
        public bool PanOnly;                             // aim sideways only, keeping the starting tilt

        public Preset Preset { get { return Preset.All.FirstOrDefault(p => p.Id == PresetId); } }

        public bool AlwaysAims
        {
            get { var p = Preset; return p != null && p.AlwaysAims; }
        }

        public RouteRecord Clone() { return (RouteRecord)MemberwiseClone(); }

        public string Summary()
        {
            var p = Preset;
            string name = p != null ? p.Name : PresetId;
            string size = "";
            if (p != null)
            {
                if (p.Kind == MoveKind.Arc || p.Kind == MoveKind.VerticalArc || p.Kind == MoveKind.Sway) size = Mathf.RoundToInt(ArcAngle) + "°";
                else if (p.Kind == MoveKind.Straight || p.Kind == MoveKind.Spiral) size = Mathf.RoundToInt(Size * 100f) + "%";
                if (Roll != 0f) size += (size.Length > 0 ? " " : "") + "roll " + Mathf.RoundToInt(Mathf.Abs(Roll)) + "°";
                if (Shake > 0f) size += (size.Length > 0 ? " " : "") + "handheld";
            }
            string ease = Curve == EaseCurve.Linear ? "Linear" : Curve + " " + (Mode == EaseMode.InOut ? "In-Out" : Mode.ToString());
            string subj = Subject == SubjectKind.Character ? CharName + " " + Bone.ToString().ToLowerInvariant()
                : Subject == SubjectKind.Pivot ? "pivot" : "no aim";
            string time = Loops > 0f ? Loops.ToString("0.#") + " loops (" + Duration.ToString("0.##") + " s)" : Duration.ToString("0.##") + " s";
            return name + (size.Length > 0 ? " " + size : "") + " · " + time + " · " + ease + " · " + subj;
        }

        // ---------------------------------------------------------------- text form

        private static string F(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        private static float P(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        public string Serialize()
        {
            var f = new[]
            {
                FormatVersion.ToString(CultureInfo.InvariantCulture),
                RouteKey.ToString(CultureInfo.InvariantCulture),
                PresetId,
                ((int)Subject).ToString(CultureInfo.InvariantCulture),
                F(Pivot.x), F(Pivot.y), F(Pivot.z),
                CharKey.ToString(CultureInfo.InvariantCulture),
                (CharName ?? "").Replace("|", "/").Replace("\n", " "),
                ((int)Bone).ToString(CultureInfo.InvariantCulture),
                F(Offset.x), F(Offset.y), F(Offset.z), F(Offset.w),
                F(ArcSign),
                F(Origin.x), F(Origin.y), F(Origin.z),
                ParentKey.ToString(CultureInfo.InvariantCulture),
                F(Size), F(ArcAngle), F(Duration),
                ((int)Curve).ToString(CultureInfo.InvariantCulture),
                ((int)Mode).ToString(CultureInfo.InvariantCulture),
                Loop ? "1" : "0",
                F(Loops),
                F(Roll), F(Shake),
                F(StartRot.x), F(StartRot.y), F(StartRot.z), F(StartRot.w),
                PanOnly ? "1" : "0",
            };
            return string.Join("|", f);
        }

        public static RouteRecord Parse(string line)
        {
            string[] f = line.Split('|');
            if (f.Length < 25 || (f[0] != "1" && f[0] != "2" && f[0] != "3" && f[0] != "4")) return null;
            int i = 1;
            var r = new RouteRecord();
            r.RouteKey = int.Parse(f[i++], CultureInfo.InvariantCulture);
            r.PresetId = f[i++];
            r.Subject = (SubjectKind)int.Parse(f[i++], CultureInfo.InvariantCulture);
            r.Pivot = new Vector3(P(f[i++]), P(f[i++]), P(f[i++]));
            r.CharKey = int.Parse(f[i++], CultureInfo.InvariantCulture);
            r.CharName = f[i++];
            r.Bone = (SubjectBone)int.Parse(f[i++], CultureInfo.InvariantCulture);
            r.Offset = new Quaternion(P(f[i++]), P(f[i++]), P(f[i++]), P(f[i++]));
            r.ArcSign = P(f[i++]);
            r.Origin = new Vector3(P(f[i++]), P(f[i++]), P(f[i++]));
            r.ParentKey = int.Parse(f[i++], CultureInfo.InvariantCulture);
            r.Size = P(f[i++]);
            r.ArcAngle = P(f[i++]);
            r.Duration = P(f[i++]);
            r.Curve = (EaseCurve)int.Parse(f[i++], CultureInfo.InvariantCulture);
            r.Mode = (EaseMode)int.Parse(f[i++], CultureInfo.InvariantCulture);
            r.Loop = f[i++] == "1";
            if (f[0] != "1" && f.Length > i) r.Loops = P(f[i++]);
            if ((f[0] == "3" || f[0] == "4") && f.Length >= i + 6)
            {
                r.Roll = P(f[i++]);
                r.Shake = P(f[i++]);
                r.StartRot = new Quaternion(P(f[i++]), P(f[i++]), P(f[i++]), P(f[i++]));
            }
            if (f[0] == "4" && f.Length > i) r.PanOnly = f[i++] == "1";
            return r;
        }
    }

    /// <summary>Records for the routes in the open scene, by route key.</summary>
    internal static class RouteRegistry
    {
        private static readonly Dictionary<int, RouteRecord> ByKey = new Dictionary<int, RouteRecord>();
        private static readonly List<RouteRecord> PendingImports = new List<RouteRecord>();

        public static RouteRecord Get(OCIRoute route)
        {
            if (route == null || route.objectInfo == null) return null;
            if (route.name == null || !route.name.StartsWith(RouteBuilder.NamePrefix)) return null;
            int key = route.objectInfo.dicKey;

            RouteRecord rec;
            if (ByKey.TryGetValue(key, out rec)) return rec;
            if (PendingImports.Count == 0) return null;

            // Imported routes get new keys: match them by where they sit and which preset they are.
            Vector3 pos = route.objectInfo.changeAmount.pos;
            for (int i = 0; i < PendingImports.Count; i++)
            {
                var p = PendingImports[i];
                var preset = p.Preset;
                if (preset == null || route.name != RouteBuilder.NamePrefix + preset.Name) continue;
                if ((p.Origin - pos).sqrMagnitude > 0.0025f) continue;
                PendingImports.RemoveAt(i);
                p.RouteKey = key;
                p.CharKey = -1;   // character keys changed too; found again by name
                p.ParentKey = -1;
                ByKey[key] = p;
                return p;
            }
            return null;
        }

        public static void Set(OCIRoute route, RouteRecord rec)
        {
            rec.RouteKey = route.objectInfo.dicKey;
            ByKey[rec.RouteKey] = rec;
        }

        public static void Remove(int key) { ByKey.Remove(key); }

        public static void Clear()
        {
            ByKey.Clear();
            PendingImports.Clear();
        }

        public static string SerializeAll()
        {
            var sb = new StringBuilder();
            var dic = Studio.Studio.Instance.dicObjectCtrl;
            foreach (var kv in ByKey)
            {
                ObjectCtrlInfo oci;
                var route = dic.TryGetValue(kv.Key, out oci) ? oci as OCIRoute : null;
                if (route == null || route.name == null || !route.name.StartsWith(RouteBuilder.NamePrefix)) continue;
                kv.Value.Origin = route.objectInfo.changeAmount.pos;
                sb.Append(kv.Value.Serialize()).Append('\n');
            }
            return sb.ToString();
        }

        public static void LoadAll(string payload)
        {
            Clear();
            foreach (var rec in ParseAll(payload)) ByKey[rec.RouteKey] = rec;
        }

        public static void AddPendingImport(string payload)
        {
            PendingImports.AddRange(ParseAll(payload));
        }

        private static IEnumerable<RouteRecord> ParseAll(string payload)
        {
            if (string.IsNullOrEmpty(payload)) yield break;
            foreach (string line in payload.Split('\n'))
            {
                if (line.Length == 0) continue;
                RouteRecord rec = null;
                try { rec = RouteRecord.Parse(line); }
                catch (Exception e) { CameraRoutesPlugin.Logger.LogWarning("Skipped a saved route record: " + e.Message); }
                if (rec != null) yield return rec;
            }
        }
    }
}
