using System.Collections.ObjectModel;
using System.Numerics;

namespace ScreenInk.Core.Models;

public sealed class LaserPath
{
    internal List<Vector2> MutablePoints { get; } = [];
    public ReadOnlyCollection<Vector2> Points { get; }
    public InkColor Color { get; }
    internal LaserPath(InkColor color) { Color = color; Points = MutablePoints.AsReadOnly(); }
}

// Temporary laser sessions never become InkStrokes or history commands.
public sealed class LaserTrail
{
    public const int MaxPoints = 8192;
    public const int MaxPaths = 128;
    public const double FadeMilliseconds = 500;
    private readonly List<LaserPath> _paths = [];
    private LaserPath? _current;
    private InkColor _contactColor;
    private double _lastActivity, _fadeStart;
    private int _fadeTotal, _removed;
    private bool _fading;
    public ReadOnlyCollection<LaserPath> Paths { get; }
    public int PointCount { get; private set; }
    public double DelayMilliseconds { get; set; } = 1200;
    public float Opacity { get; private set; } = 1;
    public bool Visible => PointCount > 0;

    public LaserTrail() { Paths = _paths.AsReadOnly(); }

    public void Begin(Vector2 point, InkColor color, double now)
    {
        new InkSample(point).Validate();
        Advance(now);
        if (_fading) { Clear(); }
        _contactColor = color;
        _current = new LaserPath(color);
        _current.MutablePoints.Add(point);
        _paths.Add(_current);
        PointCount++;
        _lastActivity = now;
        while (_paths.Count > MaxPaths) { RemoveOldest(_paths[0].Points.Count); }
        Trim();
    }

    public void Move(Vector2 point, double now)
    {
        new InkSample(point).Validate();
        Advance(now);
        if (_fading || _current is null) { Begin(point, _contactColor, now); return; }
        _lastActivity = now;
        if (_current.Points.Count > 0 && Vector2.DistanceSquared(_current.Points[^1], point) < .25f) { return; }
        _current.MutablePoints.Add(point);
        PointCount++;
        Trim();
    }

    public void End(double now)
    {
        _current = null;
        if (!_fading && Visible) { _lastActivity = now; }
    }

    public bool Advance(double now)
    {
        if (!Visible) { return false; }
        if (!_fading) {
            if (now < _lastActivity + DelayMilliseconds) { return false; }
            _fading = true;
            _fadeStart = _lastActivity + DelayMilliseconds;
            _fadeTotal = PointCount;
            _removed = 0;
        }
        var progress = Math.Clamp((now - _fadeStart) / FadeMilliseconds, 0, 1);
        var eased = progress * progress;
        var target = (int)Math.Floor(_fadeTotal * eased);
        RemoveOldest(target - _removed);
        _removed = target;
        Opacity = (float)(1 - eased);
        if (progress >= 1) { Clear(); }
        return true;
    }

    private void Trim() => RemoveOldest(PointCount - MaxPoints);

    private void RemoveOldest(int count)
    {
        while (count > 0 && _paths.Count > 0) {
            var path = _paths[0];
            var remove = Math.Min(count, path.Points.Count);
            path.MutablePoints.RemoveRange(0, remove);
            PointCount -= remove;
            count -= remove;
            if (path.Points.Count == 0) {
                _paths.RemoveAt(0);
                if (_current == path) { _current = null; }
            }
        }
    }

    public void Clear()
    {
        _paths.Clear();
        _current = null;
        PointCount = 0;
        _fading = false;
        Opacity = 1;
    }
}
