using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace Orbit;

// Dispatcher/render callback timing, not a claim about GPU-presented frame rate.
internal sealed class FrameMeter
{
    private readonly string _path;
    private readonly Stopwatch _watch = new();
    private readonly List<double> _gaps = new();
    private string _name = "";
    private double _last, _preparation;
    private TimeSpan _lastRenderTime;
    private bool _active;
    public FrameMeter(string directory) => _path = Path.Combine(directory, "performance.log");
    public void Begin(string name)
    {
        if (_active) End("interrupted");
        _name = name; _gaps.Clear(); _last = 0; _lastRenderTime = TimeSpan.MinValue; _preparation = 0;
        _watch.Restart();
    }
    public void AnimationStarted()
    {
        _preparation = _watch.Elapsed.TotalMilliseconds; _last = _preparation; _active = true;
        CompositionTarget.Rendering += OnRendering;
    }
    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs rendering)
        {
            if (rendering.RenderingTime == _lastRenderTime) return;
            _lastRenderTime = rendering.RenderingTime;
        }
        double now = _watch.Elapsed.TotalMilliseconds;
        if (_last > 0) _gaps.Add(now - _last);
        _last = now;
    }
    public void End(string result = "complete")
    {
        CompositionTarget.Rendering -= OnRendering; _active = false; _watch.Stop();
        var gaps = _gaps.OrderBy(x => x).ToArray();
        double p95 = gaps.Length == 0 ? 0 : gaps[Math.Clamp((int)Math.Ceiling(gaps.Length * .95) - 1, 0, gaps.Length - 1)];
        string line = $"{DateTime.Now:O} {_name} {result} tier={RenderCapability.Tier >> 16} prepare_ms={_preparation:F1} elapsed_ms={_watch.Elapsed.TotalMilliseconds:F1} callbacks={gaps.Length} median_gap_ms={(gaps.Length == 0 ? 0 : gaps[gaps.Length / 2]):F1} p95_gap_ms={p95:F1} max_gap_ms={(gaps.Length == 0 ? 0 : gaps[^1]):F1} gaps_over_25ms={gaps.Count(x => x > 25)}";
        try { File.AppendAllText(_path, line + Environment.NewLine); } catch { }
    }
}
