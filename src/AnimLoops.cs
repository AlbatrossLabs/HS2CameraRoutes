using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    public enum DurationMode
    {
        Seconds,
        AnimationLoops,
    }

    /// <summary>Character animation timing: loop length, and restarting so camera moves line up with loops.</summary>
    internal static class AnimLoops
    {
        /// <summary>Seconds one loop of the character's current animation takes at its current speed, or 0.</summary>
        public static float LoopSeconds(OCIChar c)
        {
            if (c == null || c.charInfo == null) return 0f;
            Animator anim = c.charInfo.animBody;
            if (anim == null || !anim.isActiveAndEnabled || anim.runtimeAnimatorController == null) return 0f;
            AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(0);
            float speed = Mathf.Abs(anim.speed);
            if (st.length <= 0.01f || speed < 0.001f) return 0f;
            return st.length / speed;
        }

        /// <summary>Restarts every character's animation from the beginning, keeping pairs in sync.</summary>
        public static void RestartAll()
        {
            foreach (var c in Subjects.Characters())
            {
                try { c.RestartAnime(); }
                catch (System.Exception e) { CameraRoutesPlugin.Logger.LogDebug("RestartAnime failed: " + e.Message); }
            }
        }
    }
}
