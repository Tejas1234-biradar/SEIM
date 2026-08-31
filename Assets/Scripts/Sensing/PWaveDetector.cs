using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Short-Term-Average / Long-Term-Average (STA/LTA) phase arrival detector.
/// Detects BOTH the P-wave arrival (first, weaker onset) and the S-wave
/// arrival (second, typically much stronger onset) from a single raw
/// acceleration stream - the classic method real seismic networks use
/// (Allen, 1978).
///
/// Real systems often use more sophisticated discrimination (polarization
/// analysis, kurtosis) to tell P and S apart - here we use the simplified
/// but legitimate approach of: first trigger = P, then look for a SECOND,
/// larger trigger after a minimum gap = S. This is a reasonable
/// simplification for a single-component (non-3-axis-polarization) virtual
/// sensor.
///
/// Pure C# class - no Unity scene dependency, testable with fake data.
/// One instance per station.
/// </summary>
public class PWaveDetector
{
    public struct Sample
    {
        public float time;
        public float amplitude;
    }

    private readonly float shortWindowSeconds;
    private readonly float longWindowSeconds;
    private readonly float pTriggerThreshold;
    private readonly float sTriggerThreshold;
    private readonly float minGapAfterPSeconds;

    private List<Sample> buffer = new List<Sample>();

    public bool HasTriggeredP { get; private set; }
    public float PWaveArrivalTime { get; private set; }

    public bool HasTriggeredS { get; private set; }
    public float SWaveArrivalTime { get; private set; }

    /// <summary>S-P interval - a classic single-station distance proxy, free once both phases are detected.</summary>
    public float SPInterval => (HasTriggeredP && HasTriggeredS) ? (SWaveArrivalTime - PWaveArrivalTime) : -1f;

    /// <param name="shortWindowSeconds">STA window (typical: 0.5-1s)</param>
    /// <param name="longWindowSeconds">LTA window (typical: 5-10s)</param>
    /// <param name="pTriggerThreshold">STA/LTA ratio for P-wave detection (typical: ~3-5, see Trnkoczy 2012)</param>
    /// <param name="sTriggerThreshold">Higher ratio required for S-wave, since S is typically much stronger than P</param>
    /// <param name="minGapAfterPSeconds">Minimum time after P-trigger before we start looking for S - avoids catching the tail of the same onset</param>
    public PWaveDetector(
        float shortWindowSeconds = 0.5f,
        float longWindowSeconds = 5f,
        float pTriggerThreshold = 4f,
        float sTriggerThreshold = 8f,
        float minGapAfterPSeconds = 0.3f)
    {
        this.shortWindowSeconds = shortWindowSeconds;
        this.longWindowSeconds = longWindowSeconds;
        this.pTriggerThreshold = pTriggerThreshold;
        this.sTriggerThreshold = sTriggerThreshold;
        this.minGapAfterPSeconds = minGapAfterPSeconds;
    }

    /// <summary>
    /// Feed one new raw sample. Returns a PhaseTrigger indicating whether P,
    /// S, or nothing new triggered on this call.
    /// </summary>
    public PhaseTrigger AddSample(float time, float amplitude)
    {
        buffer.Add(new Sample { time = time, amplitude = Mathf.Abs(amplitude) });

        float cutoff = time - longWindowSeconds;
        buffer.RemoveAll(s => s.time < cutoff);

        if (HasTriggeredP && HasTriggeredS) return PhaseTrigger.None; // both already found

        float sta = AverageOverWindow(time, shortWindowSeconds);
        float lta = AverageOverWindow(time, longWindowSeconds);
        if (lta < 0.0001f) return PhaseTrigger.None;

        float ratio = sta / lta;

        if (!HasTriggeredP)
        {
            if (ratio >= pTriggerThreshold)
            {
                HasTriggeredP = true;
                PWaveArrivalTime = time;
                return PhaseTrigger.PWave;
            }
            return PhaseTrigger.None;
        }

        // P already found - now look for the (typically much stronger) S arrival,
        // but only once we're past the minimum gap so we don't re-trigger on
        // the tail end of the P onset itself.
        if (!HasTriggeredS && (time - PWaveArrivalTime) >= minGapAfterPSeconds)
        {
            if (ratio >= sTriggerThreshold)
            {
                HasTriggeredS = true;
                SWaveArrivalTime = time;
                return PhaseTrigger.SWave;
            }
        }

        return PhaseTrigger.None;
    }

    private float AverageOverWindow(float now, float windowSeconds)
    {
        float sum = 0f;
        int count = 0;
        float cutoff = now - windowSeconds;

        foreach (var s in buffer)
        {
            if (s.time >= cutoff)
            {
                sum += s.amplitude;
                count++;
            }
        }

        return count > 0 ? sum / count : 0f;
    }

    /// <summary>Reset for reuse between separate quake events.</summary>
    public void Reset()
    {
        buffer.Clear();
        HasTriggeredP = false;
        PWaveArrivalTime = 0f;
        HasTriggeredS = false;
        SWaveArrivalTime = 0f;
    }
}

public enum PhaseTrigger { None, PWave, SWave }