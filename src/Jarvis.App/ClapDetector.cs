using Jarvis.Core;

namespace Jarvis.App;

internal sealed class ClapDetector
{
    private readonly Queue<DateTimeOffset> _clapTimesUtc = new();
    private readonly TimeSpan _cooldown;
    private readonly TimeSpan _maximumInterval;
    private readonly TimeSpan _minimumInterval;
    private readonly int _requiredClapCount;
    private readonly double _threshold;
    private DateTimeOffset _lastActivationUtc = DateTimeOffset.MinValue;

    public ClapDetector(JarvisOptions options)
    {
        _threshold = Math.Clamp(options.ClapShortcutThreshold, 0.05, 1.0);
        _requiredClapCount = Math.Max(2, options.ClapShortcutRequiredClapCount);
        _minimumInterval = TimeSpan.FromMilliseconds(Math.Max(0, options.ClapShortcutMinimumIntervalMilliseconds));
        _maximumInterval = TimeSpan.FromMilliseconds(
            Math.Max(
                options.ClapShortcutMinimumIntervalMilliseconds + 1,
                options.ClapShortcutMaximumIntervalMilliseconds));
        _cooldown = TimeSpan.FromMilliseconds(Math.Max(250, options.ClapShortcutCooldownMilliseconds));
    }

    public bool ProcessSample(double peakLevel, DateTimeOffset timestampUtc)
    {
        if (peakLevel < _threshold)
        {
            return false;
        }

        if (timestampUtc - _lastActivationUtc < _cooldown)
        {
            return false;
        }

        while (_clapTimesUtc.Count > 0 && timestampUtc - _clapTimesUtc.Peek() > _maximumInterval)
        {
            _clapTimesUtc.Dequeue();
        }

        if (_clapTimesUtc.Count > 0)
        {
            var lastClapUtc = _clapTimesUtc.Last();

            if (timestampUtc - lastClapUtc < _minimumInterval)
            {
                return false;
            }
        }

        _clapTimesUtc.Enqueue(timestampUtc);

        if (_clapTimesUtc.Count < _requiredClapCount)
        {
            return false;
        }

        var firstClapUtc = _clapTimesUtc.Peek();
        var lastRequiredClapUtc = _clapTimesUtc.Last();

        if (lastRequiredClapUtc - firstClapUtc > _maximumInterval)
        {
            _clapTimesUtc.Dequeue();
            return false;
        }

        _clapTimesUtc.Clear();
        _lastActivationUtc = timestampUtc;
        return true;
    }
}
