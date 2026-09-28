namespace TollPlaza
{
    /// <summary>
    /// One lane's exit gate and signal: the arm rises (and the signal turns green) as a vehicle comes up to the
    /// gate, stays up while any vehicle or trailer is at it, then comes back down (signal red) a moment after the
    /// last one is through. A vehicle arriving while the arm is coming down sends it back up from where it is.
    /// Simulation time, so it freezes when the game is paused and speeds up with it. Not saved.
    /// </summary>
    public class LaneGate
    {
        public const float RaiseSeconds = 0.8f;
        /// <summary>How long the arm stays up after the last vehicle stopped asking for it.</summary>
        public const float HoldSeconds = 0.5f;
        public const float LowerSeconds = 1.0f;
        private const float FramesPerSecond = 60f;

        private uint m_OpenedFrame;
        private bool m_Opened;

        /// <summary>
        /// A vehicle is coming up to the gate or is still at it: raise the arm, or keep it up. Called every
        /// update while that's so; the hold starts counting from the last call.
        /// </summary>
        public void Open(uint frame)
        {
            float t = Seconds(frame);
            if (t >= 0f && t < RaiseSeconds)
            {
                return; // already on its way up
            }
            // Restart the raise from where the arm is now (all the way up: restart the hold).
            m_OpenedFrame = frame - (uint)(Progress(frame) * RaiseSeconds * FramesPerSecond);
            m_Opened = true;
        }

        /// <summary>0 = arm down, 1 = arm up. <paramref name="frame"/> may include the fraction of the current frame.</summary>
        public float Openness(double frame)
        {
            float x = Progress(frame);
            return x * x * (3f - 2f * x); // ease in and out
        }

        /// <summary>The signal is green from the moment the arm starts up until it starts back down.</summary>
        public bool Green(double frame)
        {
            float t = Seconds(frame);
            return t >= 0f && t < RaiseSeconds + HoldSeconds;
        }

        /// <summary>How far up the arm is, linear in time (0 to 1).</summary>
        private float Progress(double frame)
        {
            float t = Seconds(frame);
            if (t < 0f) return 0f;
            if (t < RaiseSeconds) return t / RaiseSeconds;
            t -= RaiseSeconds;
            if (t < HoldSeconds) return 1f;
            t -= HoldSeconds;
            if (t < LowerSeconds) return 1f - t / LowerSeconds;
            return 0f;
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
    }
}
