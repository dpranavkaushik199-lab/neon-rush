using UnityEngine;

namespace NeonRunner
{
    public enum TimingGrade { Miss, Good, Perfect }

    /// <summary>
    /// Grades a button press against a "peak" moment. The window spans
    /// [peak - before, peak + after]; presses close to the peak are Perfect.
    /// Peaks can be registered ahead of time (predicted) or retroactively, so a
    /// press recorded slightly before the window opened still counts.
    /// </summary>
    [System.Serializable]
    public class TimedInputWindow
    {
        public float before = 0.15f;
        public float after = 0.2f;
        public float perfectTolerance = 0.08f;

        float peakTime = float.NegativeInfinity;

        public bool HasPeak => !float.IsNegativeInfinity(peakTime);
        public float PeakTime => peakTime;

        public void Configure(float windowBefore, float windowAfter, float tolerance)
        {
            before = windowBefore;
            after = windowAfter;
            perfectTolerance = tolerance;
        }

        public void SetPeak(float time) => peakTime = time;

        public void Clear() => peakTime = float.NegativeInfinity;

        public bool IsOpenAt(float time) => HasPeak && time >= peakTime - before && time <= peakTime + after;

        /// <summary>True once the window can no longer include a press made at <paramref name="pressTime"/>.</summary>
        public bool HasExpiredFor(float pressTime, float now) => now > pressTime + before && !IsOpenAt(pressTime);

        public TimingGrade Evaluate(float pressTime)
        {
            if (!IsOpenAt(pressTime)) return TimingGrade.Miss;
            return Mathf.Abs(pressTime - peakTime) <= perfectTolerance ? TimingGrade.Perfect : TimingGrade.Good;
        }
    }
}
