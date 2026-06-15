namespace Core
{
    public sealed class FrameInfo
    {
        public float[] Lpc = default!;
        public float   Gain;
        public bool    Voiced;
        public int     PitchPeriod;
    }
}