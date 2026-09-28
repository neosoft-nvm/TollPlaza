namespace TollPlaza
{
    /// <summary>
    /// One lane's exit gate and signal: the arm rises (and the signal turns green) as a vehicle comes up to the
    /// gate, stays up while it drives through, then comes back down (signal red). A vehicle arriving while the
    /// arm is still up or coming down keeps it moving from where it is. Simulation time, so it freezes when the
    /// game is paused and speeds up with it. Not saved.
    /// </summary>
    public class LaneGate
    {
        public const float RaiseSeconds = 0.8f;
        public const float HoldSeconds = 1.5f;
        public const float LowerSeconds = 1.0f;
        private const float FramesPerSecond = 60f;

        private uint m_OpenedFrame;
        private bool m_Opened;

        /// <summary>A vehicle is about to reach the gate (the arm takes RaiseSeconds to go up).</summary>
        public void Open(uint frame)
        {
            float up = Openness(frame);
            // Pretend the arm started rising just long enough ago to be where it is now; if it's already all
            // the way up this restarts the hold.
            m_OpenedFrame = frame - (uint)(up * RaiseSeconds * FramesPerSecond);
            m_Opened = true;
        }

        /// <summary>0 = arm down, 1 = arm up. <paramref name="frame"/> may include the fraction of the current frame.</summary>
        public float Openness(double frame)
        {
            float t = Seconds(frame);
            if (t < 0f) return 0f;
            if (t < RaiseSeconds) return Ease(t / RaiseSeconds);
            t -= RaiseSeconds;
            if (t < HoldSeconds) return 1f;
            t -= HoldSeconds;
            if (t < LowerSeconds) return Ease(1f - t / LowerSeconds);
            return 0f;
        }

        /// <summary>The signal is green from the moment the arm starts up until it starts back down.</summary>
        public bool Green(double frame)
        {
            float t = Seconds(frame);
            return t >= 0f && t < RaiseSeconds + HoldSeconds;
        }

        private float Seconds(double frame)
        {
            if (!m_Opened)
            {
                return -1f;
            }
            // Relative to the opening frame so large frame numbers keep their precision.
            double since = frame - m_OpenedFrame;
            return since > 1e6 ? float.MaxValue : (float)(since / FramesPerSecond);
        }

        private static float Ease(float x) => x * x * (3f - 2f * x);
    }
}
