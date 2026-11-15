using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace GestirnDev
{
internal static class Ease
{
    public static float OutCubic(float t) { t = 1f - t; return 1f - t * t * t; }
    public static float InOutCubic(float t) { return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3) / 2f; }
    public static float OutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
    public static float Linear(float t) { return t; }
}

// One shared ~60 fps timer. It only runs while a tween or ticker is active,
// so an idle editor costs no CPU.
internal static class Animator
{
    private static readonly Timer timer = new Timer { Interval = 15 };
    private static readonly List<AnimatedFloat> active = new List<AnimatedFloat>();
    private static readonly List<Action<double>> tickers = new List<Action<double>>();
    private static readonly Stopwatch clock = Stopwatch.StartNew();

    // When true (rich animations off in Settings) every tween jumps straight to its end.
    public static bool Reduced;

    static Animator()
    {
        timer.Tick += (_, _) => Tick();
    }

    public static double Now { get { return clock.Elapsed.TotalMilliseconds; } }

    internal static void Register(AnimatedFloat value)
    {
        if (!active.Contains(value)) active.Add(value);
        timer.Enabled = true;
    }

    // A ticker runs every frame until removed (spinners, caret blink).
    public static void AddTicker(Action<double> ticker)
    {
        if (!tickers.Contains(ticker)) tickers.Add(ticker);
        timer.Enabled = true;
    }

    public static void RemoveTicker(Action<double> ticker)
    {
        tickers.Remove(ticker);
        if (active.Count == 0 && tickers.Count == 0) timer.Enabled = false;
    }

    private static void Tick()
    {
        double now = Now;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (i >= active.Count) continue;
            var value = active[i];
            if (!value.Step(now)) active.Remove(value);
        }
        foreach (var ticker in tickers.ToArray())
        {
            try { ticker(now); }
            catch (ObjectDisposedException) { tickers.Remove(ticker); }
        }
        if (active.Count == 0 && tickers.Count == 0) timer.Enabled = false;
    }
}

// A float that glides to its target. Changed fires on every frame so the owner can repaint or relayout.
internal sealed class AnimatedFloat
{
    private float from, to;
    private double start, duration;
    private Func<float, float> easing = Ease.OutCubic;

    public AnimatedFloat(float initial) { Value = from = to = initial; }

    public float Value { get; private set; }
    public float Target { get { return to; } }
    public bool Running { get; private set; }
    public event Action Changed;
    public event Action Finished;

    public void To(float target, int milliseconds, Func<float, float> ease = null, int delay = 0)
    {
        if (Animator.Reduced)
        {
            Value = from = to = target;
            Running = false;
            if (Changed != null) Changed();
            if (Finished != null) Finished();
            return;
        }
        if (Math.Abs(target - to) < 0.0001f && Running) return;
        if (Math.Abs(target - Value) < 0.0001f && !Running) { to = target; return; }
        from = Value;
        to = target;
        start = Animator.Now + delay;
        duration = Math.Max(1, milliseconds);
        easing = ease ?? Ease.OutCubic;
        Running = true;
        Animator.Register(this);
    }

    public void Snap(float value)
    {
        Value = from = to = value;
        Running = false;
        if (Changed != null) Changed();
    }

    internal bool Step(double now)
    {
        if (!Running) return false;
        if (now < start) return true;
        float t = (float)Math.Min(1.0, (now - start) / duration);
        Value = from + (to - from) * easing(t);
        if (t >= 1f) { Value = to; Running = false; }
        if (Changed != null) Changed();
        if (!Running && Finished != null) Finished();
        return Running;
    }
}
}
