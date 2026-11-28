using Glass.Core.Logging;
using Glass.World;
using System;
using System.Collections.Generic;

namespace Glass.Agents.Healing;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WardWatch
//
// The healing agent's live state for one ward: an estimate of the rate at which the ward is losing HP, the attackers
// currently engaging it, and the HP thresholds whose crossing must be reported.  HP samples feed a net loss rate, an
// exponentially weighted moving average of HP lost per second with time constant TauSeconds, so healing lowers the
// rate and a single hit is smoothed rather than taken as a trend.   Thresholds are HP values; each is reported
// once, the first time a sample is at or below it, and is then removed.  The ward's last known position is held as
// well.  All state is guarded by one lock.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class WardWatch
{
    private const LogLevel defaultLoglevel = LogLevel.Info;
    private const double TauSeconds = 8.0;

    private readonly object _lock = new object();
    private readonly EntityId _ward;
    private Position? _position = null;

    private bool _hasSample = false;
    private long _lastHp = 0;
    private DateTime _lastSampleTime;
    private double _netLossRate = 0.0;

    private readonly List<long> _thresholds = new List<long>();
    private readonly Dictionary<EntityId, DateTime> _lastDamageByAttacker = new Dictionary<EntityId, DateTime>();
    private readonly List<EntityId> _staleAttackers = new List<EntityId>();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WardWatch (constructor)
    //
    // Creates the watch for one ward with no samples, no thresholds and no attackers.
    //
    // ward:  The EntityId of the ward.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public WardWatch(EntityId ward)
    {
        _ward = ward;
        DebugLog.Write(LogChannel.Healing, "WardWatch: created for ward " + _ward + ".", defaultLoglevel);
    }

    public EntityId Ward => _ward;

    public Position? Position
    {
        get
        {
            lock (_lock)
            {
                return _position;
            }
        }
    }

    public double NetLossRate
    {
        get
        {
            lock (_lock)
            {
                return _netLossRate;
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Sample
    //
    // Records the ward's current HP.  The first sample only sets the baseline.  Otherwise the HP lost per second since the
    // last sample is folded into the net loss rate with a weight of 1 - exp(-elapsed / TauSeconds); a sample whose time is
    // not after the last sample's updates the HP but not the rate.  Every threshold at or above the sample's HP is then
    // removed and reported, highest first.  This method is the only writer of the sample state and must be called from
    // one thread, so it reads that state without the lock; the lock covers only the writes and the threshold removal.
    //
    // currentHp:  The ward's current HP.  May be negative.
    // time:       The time of the packet that carried the HP.
    // crossed:    Receives the thresholds crossed by this sample; cleared first.
    //
    // Returns the number of thresholds crossed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint Sample(long currentHp, DateTime time, List<long> crossed)
    {
        crossed.Clear();
        bool baseline = !_hasSample;
        bool outOfOrder = false;
        DateTime newSampleTime = time;
        double newRate = _netLossRate;

        if (!baseline)
        {
            double elapsed = (time - _lastSampleTime).TotalSeconds;
            if (elapsed > 0.0)
            {
                double lossPerSecond = (double)(_lastHp - currentHp) / elapsed;
                double weight = 1.0 - Math.Exp(-elapsed / TauSeconds);
                newRate = _netLossRate + weight * (lossPerSecond - _netLossRate);
            }
            else
            {
                outOfOrder = true;
                newSampleTime = _lastSampleTime;
            }
        }

        lock (_lock)
        {
            _hasSample = true;
            _lastHp = currentHp;
            _lastSampleTime = newSampleTime;
            _netLossRate = newRate;

            while (_thresholds.Count > 0 && currentHp <= _thresholds[0])
            {
                crossed.Add(_thresholds[0]);
                _thresholds.RemoveAt(0);
            }
        }

        if (baseline)
        {
            DebugLog.Write(LogChannel.Healing, "WardWatch.Sample: ward " + _ward + " baseline hp=" + currentHp
                + ".", defaultLoglevel);
        }

        if (outOfOrder)
        {
            DebugLog.Write(LogChannel.Healing, "WardWatch.Sample: ward " + _ward + " hp=" + currentHp + " at "
                + time.ToString("HH:mm:ss.fff") + " is not after the last sample, rate unchanged.", defaultLoglevel);
        }

        for (int crossedIndex = 0; crossedIndex < crossed.Count; crossedIndex++)
        {
            DebugLog.Write(LogChannel.Healing, "WardWatch.Sample: ward " + _ward + " hp=" + currentHp
                + " crossed threshold " + crossed[crossedIndex] + ".", defaultLoglevel);
        }

        return (uint)crossed.Count;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetPosition
    //
    // Records the ward's current position, replacing the position held before.
    //
    // position:  The ward's new position.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetPosition(Position position)
    {
        bool first;
        lock (_lock)
        {
            first = (_position == null);
            _position = position;
        }

        if (first)
        {
            DebugLog.Write(LogChannel.Healing, "WardWatch.SetPosition: ward " + _ward + " first position ("
                + position.X.ToString("R") + "," + position.Y.ToString("R") + "," + position.Z.ToString("R")
                + ").", defaultLoglevel);
            return;
        }

        DebugLog.Write(LogChannel.Healing, "WardWatch.SetPosition: ward " + _ward + " moved to ("
            + position.X.ToString("R") + "," + position.Y.ToString("R") + "," + position.Z.ToString("R")
            + ").", defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddThreshold
    //
    // Adds an HP threshold to be reported when a sample is at or below it.  Thresholds are held highest first.  A
    // threshold already held is not added again.
    //
    // hp:  The HP value of the threshold.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void AddThreshold(long hp)
    {
        bool alreadyHeld = false;
        int insertAt = 0;

        lock (_lock)
        {
            while (insertAt < _thresholds.Count && _thresholds[insertAt] > hp)
            {
                insertAt++;
            }

            if (insertAt < _thresholds.Count && _thresholds[insertAt] == hp)
            {
                alreadyHeld = true;
            }
            else
            {
                _thresholds.Insert(insertAt, hp);
            }
        }

        if (alreadyHeld)
        {
            DebugLog.Write(LogChannel.Healing, "WardWatch.AddThreshold: ward " + _ward + " already holds threshold "
                + hp + ".", defaultLoglevel);
            return;
        }

        DebugLog.Write(LogChannel.Healing, "WardWatch.AddThreshold: ward " + _ward + " threshold " + hp + " added.",
            defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryPredictHp
    //
    // Predicts the ward's HP at a future time by extending the last sample at the net loss rate.  A time before the last
    // sample predicts the last sample's HP.
    //
    // at:           The time to predict for.
    // predictedHp:  Receives the predicted HP, or 0 when there is no sample.
    //
    // Returns true when a prediction was made, false when the ward has no sample yet.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryPredictHp(DateTime at, out long predictedHp)
    {
        bool hasSample;
        long lastHp;
        DateTime lastSampleTime;
        double netLossRate;

        lock (_lock)
        {
            hasSample = _hasSample;
            lastHp = _lastHp;
            lastSampleTime = _lastSampleTime;
            netLossRate = _netLossRate;
        }

        if (!hasSample)
        {
            predictedHp = 0;
            DebugLog.Write(LogChannel.Healing, "WardWatch.TryPredictHp: ward " + _ward + " has no sample.",
                defaultLoglevel);
            return false;
        }

        double ahead = (at - lastSampleTime).TotalSeconds;
        if (ahead < 0.0)
        {
            ahead = 0.0;
        }

        predictedHp = lastHp - (long)Math.Round(netLossRate * ahead);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RecordDamage
    //
    // Records that an attacker damaged the ward.  An attacker not already held is logged as new.
    //
    // attacker:  The EntityId of the attacker.
    // time:      The time of the packet that carried the swing.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void RecordDamage(EntityId attacker, DateTime time)
    {
        bool isNew;
        lock (_lock)
        {
            isNew = !_lastDamageByAttacker.ContainsKey(attacker);
            _lastDamageByAttacker[attacker] = time;
        }

        if (isNew)
        {
            DebugLog.Write(LogChannel.Healing, "WardWatch.RecordDamage: ward " + _ward + " new attacker " + attacker
                + " at " + time.ToString("HH:mm:ss.fff") + ".", defaultLoglevel);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ActiveAttackers
    //
    // Lists the attackers whose last damage is within the idle limit of the given time, and drops every attacker whose
    // last damage is older.
    //
    // now:        The time to measure from.
    // idleLimit:  How long after its last damage an attacker is still counted.
    // attackers:  Receives the active attackers; cleared first.
    //
    // Returns the number of active attackers.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint ActiveAttackers(DateTime now, TimeSpan idleLimit, List<EntityId> attackers)
    {
        attackers.Clear();
        uint dropped = 0;

        lock (_lock)
        {
            foreach (KeyValuePair<EntityId, DateTime> entry in _lastDamageByAttacker)
            {
                if (now - entry.Value > idleLimit)
                {
                    _staleAttackers.Add(entry.Key);
                }
                else
                {
                    attackers.Add(entry.Key);
                }
            }

            for (int staleIndex = 0; staleIndex < _staleAttackers.Count; staleIndex++)
            {
                _lastDamageByAttacker.Remove(_staleAttackers[staleIndex]);
                dropped++;
            }
            _staleAttackers.Clear();
        }

        if (dropped > 0)
        {
            DebugLog.Write(LogChannel.Healing, "WardWatch.ActiveAttackers: ward " + _ward + " dropped " + dropped
                + " idle attackers, " + attackers.Count + " active.", defaultLoglevel);
        }

        return (uint)attackers.Count;
    }
}