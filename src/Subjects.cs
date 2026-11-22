using System.Collections.Generic;
using System.Linq;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    /// <summary>Finds characters and their head / chest / hips bones for subject tracking.</summary>
    internal static class Subjects
    {
        private static readonly string[][] BoneNames =
        {
            new[] { "cf_J_Head", "cm_J_Head" },
            new[] { "cf_J_Spine03", "cm_J_Spine03" },
            new[] { "cf_J_Hips", "cm_J_Hips" },
        };

        private static readonly Dictionary<int, Transform> Cache = new Dictionary<int, Transform>();
        private static readonly HashSet<int> Fallbacks = new HashSet<int>();

        /// <summary>For the window: which transform is actually followed ("!" first when it isn't the bone asked for).</summary>
        public static string Describe(OCIChar c, SubjectBone bone)
        {
            Transform t = Bone(c, bone);
            if (t == null) return null;
            return Fallbacks.Contains(c.objectInfo.dicKey * 4 + (int)bone) ? "!" + t.name : t.name;
        }

        public static IEnumerable<OCIChar> Characters()
        {
            return Studio.Studio.Instance.dicObjectCtrl.Values.OfType<OCIChar>();
        }

        public static OCIChar ByKey(int key)
        {
            ObjectCtrlInfo oci;
            return key >= 0 && Studio.Studio.Instance.dicObjectCtrl.TryGetValue(key, out oci) ? oci as OCIChar : null;
        }

        public static OCIChar ByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Characters().FirstOrDefault(c => Name(c) == name);
        }

        public static string Name(OCIChar c)
        {
            return c != null && c.treeNodeObject != null ? c.treeNodeObject.textName : "";
        }

        public static Transform Bone(OCIChar c, SubjectBone bone)
        {
            if (c == null || c.charInfo == null) return null;
            int key = c.objectInfo.dicKey * 4 + (int)bone;
            Transform t;
            if (Cache.TryGetValue(key, out t) && t != null) return t;

            Transform root = c.charInfo.objBodyBone != null ? c.charInfo.objBodyBone.transform : c.charInfo.transform;
            t = null;
            foreach (string n in BoneNames[(int)bone])
            {
                t = FindDeep(root, n);
                if (t != null) break;
            }
            if (t == null)
            {
                t = c.charInfo.transform; // odd rigs: at least follow the character
                Fallbacks.Add(key);
                CameraRoutesPlugin.Logger.LogWarning("No " + string.Join(" / ", BoneNames[(int)bone]) + " bone on " + Name(c) + "; following the character's root instead.");
            }
            else Fallbacks.Remove(key);
            Cache[key] = t;
            return t;
        }

        /// <summary>The character whose bone sits closest to the line of sight (in front of the view).</summary>
        public static OCIChar Nearest(Vector3 origin, Vector3 forward, SubjectBone bone)
        {
            OCIChar best = null;
            float bestScore = float.MaxValue;
            foreach (var c in Characters())
            {
                Transform t = Bone(c, bone);
                if (t == null) continue;
                Vector3 to = t.position - origin;
                float along = Vector3.Dot(to, forward);
                float off = (to - forward * along).magnitude;
                float score = along > 0f ? off : off + 1000f; // behind the camera only as a last resort
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        /// <summary>The next character after <paramref name="current"/>, for cycling in the window.</summary>
        public static OCIChar Next(OCIChar current)
        {
            var list = Characters().OrderBy(c => c.objectInfo.dicKey).ToList();
            if (list.Count == 0) return null;
            int i = current == null ? -1 : list.IndexOf(current);
            return list[(i + 1) % list.Count];
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
