using Glass.Core.Logging;

namespace Glass.Input;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ChordDetector
//
// Detects a two-key chord on a single device instance while passing all other
// key traffic through untouched.  A chord-member key press is deferred for a
// short window.  If the partner key is pressed within the window, the chord
// fires and both keys are swallowed, including their releases.  Otherwise the
// deferred press is released downstream in its original order.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class ChordDetector
{
    private readonly int _windowMs;
    private readonly Action<HidKeyEventArgs> _emit;
    private readonly object _stateLock = new object();

    // Registered chords.  A null ChordAction means the chord is recognized and swallowed with no further effect.
    private readonly List<(string First, string Second, Action? ChordAction, string Name)> _chords = new();

    // Every key that participates in any registered chord
    private readonly HashSet<string> _memberKeys = new HashSet<string>();

    // The chord-member press currently being deferred, null when none
    private HidKeyEventArgs? _pendingPress;

    // One-shot timer that flushes the pending press when the window expires
    private readonly System.Threading.Timer _windowTimer;

    // Keys whose press was consumed by a chord — their releases are swallowed
    private readonly HashSet<string> _swallowedKeys = new HashSet<string>();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ChordDetector
    //
    // windowMs:  Deferral window in milliseconds
    // emit:      Called to deliver a key event downstream for normal dispatch
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ChordDetector(int windowMs, Action<HidKeyEventArgs> emit)
    {
        _windowMs = windowMs;
        _emit = emit;
        _windowTimer = new System.Threading.Timer(OnWindowExpired, null, Timeout.Infinite, Timeout.Infinite);

        DebugLog.Write(LogChannel.Input, $"ChordDetector: created, window={windowMs}ms.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddChord
    //
    // Registers a two-key chord.  A null chordAction means the chord is
    // swallowed when detected but triggers nothing.
    //
    // firstKey:     Name of the first chord member key e.g. "X-1"
    // secondKey:    Name of the second chord member key e.g. "X-2"
    // chordAction:  Called when the chord fires, or null to swallow only
    // name:         Chord name used in log messages
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void AddChord(string firstKey, string secondKey, Action? chordAction, string name)
    {
        _chords.Add((firstKey, secondKey, chordAction, name));
        _memberKeys.Add(firstKey);
        _memberKeys.Add(secondKey);

        DebugLog.Write(LogChannel.Input, $"ChordDetector.AddChord: '{name}' = '{firstKey}'+'{secondKey}' action={(chordAction != null ? "set" : "none")}.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HandleKey
    //
    // Entry point for every key event from the device instance this detector
    // serves.  Keys that are not chord members pass straight through to the
    // emit callback.  A chord-member press is deferred; if a second press
    // completes a registered chord within the window, the chord fires and both
    // keys are swallowed including their releases.  Otherwise the deferred
    // press is flushed in arrival order.
    //
    // e:  The key event to process
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void HandleKey(HidKeyEventArgs e)
    {
        lock (_stateLock)
        {
            if (!e.IsPressed)
            {
                if (_swallowedKeys.Remove(e.KeyName))
                {
                    DebugLog.Write(LogChannel.Input, $"ChordDetector.HandleKey: swallowing release of chord key '{e.KeyName}'.", LogLevel.Trace);
                    return;
                }

                if ((_pendingPress != null) && (_pendingPress.KeyName == e.KeyName))
                {
                    // Released before a partner arrived — a chord is no longer possible, flush the tap
                    DebugLog.Write(LogChannel.Input, $"ChordDetector.HandleKey: '{e.KeyName}' released alone, flushing tap.", LogLevel.Trace);
                    FlushPendingLocked();
                }

                _emit(e);
                return;
            }

            if (_pendingPress != null)
            {
                foreach ((string first, string second, Action? chordAction, string name) in _chords)
                {
                    bool matched = ((first == _pendingPress.KeyName) && (second == e.KeyName))
                                || ((second == _pendingPress.KeyName) && (first == e.KeyName));

                    if (!matched)
                    {
                        continue;
                    }

                    DebugLog.Write(LogChannel.Input, $"ChordDetector.HandleKey: chord '{name}' fired.", LogLevel.Trace);

                    _windowTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    _swallowedKeys.Add(_pendingPress.KeyName);
                    _swallowedKeys.Add(e.KeyName);
                    _pendingPress = null;

                    if (chordAction != null)
                    {
                        chordAction();
                    }
                    else
                    {
                        DebugLog.Write(LogChannel.Input, $"ChordDetector.HandleKey: chord '{name}' swallowed, no action.", LogLevel.Trace);
                    }

                    return;
                }

                // Not a chord partner — release the deferred press ahead of this key to preserve order
                DebugLog.Write(LogChannel.Input, $"ChordDetector.HandleKey: '{e.KeyName}' is not a partner, flushing pending press.", LogLevel.Trace);
                FlushPendingLocked();
            }

            if (_memberKeys.Contains(e.KeyName))
            {
                DebugLog.Write(LogChannel.Input, $"ChordDetector.HandleKey: deferring '{e.KeyName}' for {_windowMs}ms.", LogLevel.Trace);
                _pendingPress = e;
                _windowTimer.Change(_windowMs, Timeout.Infinite);
                return;
            }

            _emit(e);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // FlushPendingLocked
    //
    // Disarms the window timer and delivers the deferred press downstream,
    // clearing the pending state.  Does nothing if no press is pending.
    // The caller must hold _stateLock.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void FlushPendingLocked()
    {
        _windowTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (_pendingPress == null)
        {
            return;
        }

        DebugLog.Write(LogChannel.Input, $"ChordDetector.FlushPendingLocked: flushing deferred press '{_pendingPress.KeyName}'.", LogLevel.Trace);

        _emit(_pendingPress);
        _pendingPress = null;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnWindowExpired
    //
    // Timer callback fired when the deferral window elapses with no partner
    // key.  Takes the state lock and flushes the deferred press.  A stale
    // firing that races with a disarm finds no pending press and does nothing.
    //
    // state:  Unused timer state
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnWindowExpired(object? state)
    {
        lock (_stateLock)
        {
            if (_pendingPress == null)
            {
                DebugLog.Write(LogChannel.Input, "ChordDetector.OnWindowExpired: stale firing, nothing pending.", LogLevel.Trace);
                return;
            }

            DebugLog.Write(LogChannel.Input, $"ChordDetector.OnWindowExpired: window elapsed, flushing '{_pendingPress.KeyName}'.", LogLevel.Trace);
            FlushPendingLocked();
        }
    }
}
