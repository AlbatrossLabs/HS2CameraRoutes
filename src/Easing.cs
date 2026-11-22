using System;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    public enum EaseCurve
    {
        Linear,
        Sine,
        Quad,
        Cubic,
        Quart,
        Quint,
        Expo,
        Circ,
        Back,
        Elastic,
        Bounce,
    }

    public enum EaseMode
    {
        InOut,
        In,
        Out,
    }

    /// <summary>
    /// The easing curves Studio's tween engine supports, by curve + mode. The math here matches the engine's
    /// (Robert Penner's equations, as in iTween) and is used for the preview and for matching speeds between
    /// segments.
    /// </summary>
    internal static class Easing
    {
        public static readonly EaseCurve[] Curves = (EaseCurve[])Enum.GetValues(typeof(EaseCurve));

        public static StudioTween.EaseType ToStudio(EaseCurve curve, EaseMode mode)
        {
            if (curve == EaseCurve.Linear) return StudioTween.EaseType.linear;
            string m = mode == EaseMode.In ? "In" : mode == EaseMode.Out ? "Out" : "InOut";
            return (StudioTween.EaseType)Enum.Parse(typeof(StudioTween.EaseType), "ease" + m + curve);
        }

        /// <summary>The easing function for one of Studio's ease types (any of them, not just the ones listed here).</summary>
        public static Func<float, float> FromStudio(StudioTween.EaseType ease)
        {
            string n = ease.ToString();
            if (n == "spring") return Spring;
            EaseMode mode;
            string rest;
            if (n.StartsWith("easeInOut")) { mode = EaseMode.InOut; rest = n.Substring(9); }
            else if (n.StartsWith("easeIn")) { mode = EaseMode.In; rest = n.Substring(6); }
            else if (n.StartsWith("easeOut")) { mode = EaseMode.Out; rest = n.Substring(7); }
            else return t => t;
            EaseCurve curve;
            try { curve = (EaseCurve)Enum.Parse(typeof(EaseCurve), rest); }
            catch (ArgumentException) { return t => t; }
            return t => Evaluate(curve, mode, t);
        }

        /// <summary>iTween's spring ease.</summary>
        private static float Spring(float t)
        {
            t = Mathf.Clamp01(t);
            return (Mathf.Sin(t * Mathf.PI * (0.2f + 2.5f * t * t * t)) * Mathf.Pow(1f - t, 2.2f) + t) * (1f + 1.2f * (1f - t));
        }

        /// <summary>Progress (0..1, can overshoot for Back and Elastic) at time t (0..1).</summary>
        public static float Evaluate(EaseCurve curve, EaseMode mode, float t)
        {
            t = Mathf.Clamp01(t);
            switch (mode)
            {
                case EaseMode.In: return In(curve, t);
                case EaseMode.Out: return 1f - In(curve, 1f - t);
                default:
                    return t < 0.5f ? In(curve, t * 2f) * 0.5f : 1f - In(curve, (1f - t) * 2f) * 0.5f;
            }
        }

        /// <summary>
        /// Speed at the end of the ease-in (same as the start of the ease-out), relative to the segment's average
        /// speed. Used so a linear segment can pick up at the speed the eased one finished at.
        /// </summary>
        public static float JoinSpeed(EaseCurve curve)
        {
            const float h = 0.001f;
            float slope = (In(curve, 1f) - In(curve, 1f - h)) / h;
            return Mathf.Clamp(slope, 0.5f, 8f);
        }

        private static float In(EaseCurve curve, float t)
        {
            switch (curve)
            {
                case EaseCurve.Sine: return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case EaseCurve.Quad: return t * t;
                case EaseCurve.Cubic: return t * t * t;
                case EaseCurve.Quart: return t * t * t * t;
                case EaseCurve.Quint: return t * t * t * t * t;
                case EaseCurve.Expo: return t <= 0f ? 0f : Mathf.Pow(2f, 10f * (t - 1f));
                case EaseCurve.Circ: return 1f - Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
                case EaseCurve.Back:
                    {
                        const float s = 1.70158f;
                        return t * t * ((s + 1f) * t - s);
                    }
                case EaseCurve.Elastic:
                    {
                        if (t <= 0f) return 0f;
                        if (t >= 1f) return 1f;
                        const float p = 0.3f, s = p / 4f;
                        float u = t - 1f;
                        return -(Mathf.Pow(2f, 10f * u) * Mathf.Sin((u - s) * (2f * Mathf.PI) / p));
                    }
                case EaseCurve.Bounce: return 1f - BounceOut(1f - t);
                default: return t;
            }
        }

        private static float BounceOut(float t)
        {
            if (t < 1f / 2.75f) return 7.5625f * t * t;
            if (t < 2f / 2.75f) { t -= 1.5f / 2.75f; return 7.5625f * t * t + 0.75f; }
            if (t < 2.5f / 2.75f) { t -= 2.25f / 2.75f; return 7.5625f * t * t + 0.9375f; }
            t -= 2.625f / 2.75f;
            return 7.5625f * t * t + 0.984375f;
        }
    }
}
