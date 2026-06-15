using System;
using System.Numerics;

namespace Core
{
    public class Modulator
    {
        public float FormantScale   { get; set; } = 1.0f;  // 1.0 = ingen förändring
        public float PitchSemitones { get; set; } = 0f;    // 0.0 = ingen förändring
        public bool  VoiceUnvoiced  { get; set; } = true;  // true = voice/unvoiced, false = viska
        public bool  UseFixedPitch  { get; set; } = false;
        public int   FixedPitchHz   { get; set; } = 80;

        private readonly int _sampleRate;

        // förallokerad scratch — inga allokeringar på ljudtråden
        private readonly Complex[] _poly;     // order + 1
        private readonly Complex[] _roots;    // order
        private readonly Complex[] _newPoly;  // order + 1
        private readonly float[]   _hBuf;     // impulssvar

        public Modulator(int sampleRate, int order, int energyWindow = 2048)
        {
            _sampleRate = sampleRate;
            _poly    = new Complex[order + 1];
            _roots   = new Complex[order];
            _newPoly = new Complex[order + 1];
            _hBuf    = new float[energyWindow];
        }

        public void ModulateInto(FrameParams dst, FrameInfo fi)
        {
            dst.Voiced = fi.Voiced && VoiceUnvoiced;

            // --- formanter + loudness-kompensation ---
            if (Math.Abs(FormantScale - 1.0f) > 1e-6f)
            {
                float eBefore = ImpulseResponseEnergy(fi.Lpc);
                ApplyFormantScaleInto(dst.Lpc, fi.Lpc, FormantScale);
                float eAfter  = ImpulseResponseEnergy(dst.Lpc);
                dst.Gain = (eAfter > 1e-9f)
                    ? fi.Gain * (float)Math.Sqrt(eBefore / eAfter)
                    : fi.Gain;
            }
            else
            {
                Array.Copy(fi.Lpc, dst.Lpc, fi.Lpc.Length);
                dst.Gain = fi.Gain;
            }

            // --- tonhöjd / period ---
            if (UseFixedPitch)
            {
                dst.Period = FixedPitchHz > 0
                    ? Math.Max(1, _sampleRate / FixedPitchHz)
                    : Math.Max(1, fi.PitchPeriod);
            }
            else
            {
                float factor = (float)Math.Pow(2.0, PitchSemitones / 12.0);
                dst.Period = Math.Max(1, (int)Math.Round(fi.PitchPeriod / factor));
            }
        }

        private float ImpulseResponseEnergy(float[] a)
        {
            int p = a.Length, n = _hBuf.Length;
            float energy = 0f;
            for (int i = 0; i < n; i++)
            {
                float acc = (i == 0) ? 1.0f : 0.0f;
                for (int k = 0; k < p; k++)
                    if (i - 1 - k >= 0) acc -= a[k] * _hBuf[i - 1 - k];
                _hBuf[i] = acc;
                energy += acc * acc;
            }
            return energy;
        }

        private void ApplyFormantScaleInto(float[] dst, float[] src, float alpha)
        {
            int p = src.Length;

            _poly[0] = Complex.One;
            for (int i = 0; i < p; i++) _poly[i + 1] = new Complex(src[i], 0.0);

            FindRootsInto(_roots, _poly);

            const double safe = Math.PI * 0.95;
            for (int i = 0; i < p; i++)
            {
                double mag = _roots[i].Magnitude;
                double ang = _roots[i].Phase * alpha;
                if (Math.Abs(ang) > safe)
                {
                    double excess = (Math.Abs(ang) - safe) / (Math.PI - safe);
                    mag *= (1.0 - 0.5 * excess);
                    ang  = Math.Sign(ang) * safe;
                }
                if (mag > 0.999) mag = 0.999;
                _roots[i] = Complex.FromPolarCoordinates(mag, ang);
            }

            _newPoly[0] = Complex.One;
            int deg = 0;
            for (int r = 0; r < p; r++)
            {
                Complex root = _roots[r];
                _newPoly[deg + 1] = -root * _newPoly[deg];
                for (int j = deg; j >= 1; j--)
                    _newPoly[j] = _newPoly[j] - root * _newPoly[j - 1];
                deg++;
            }

            for (int i = 0; i < p; i++) dst[i] = (float)_newPoly[i + 1].Real;
        }

        private void FindRootsInto(Complex[] roots, Complex[] poly)
        {
            int n = poly.Length - 1;
            var seed = new Complex(0.4, 0.9);
            Complex cur = Complex.One;
            for (int i = 0; i < n; i++) { cur *= seed; roots[i] = cur; }

            for (int iter = 0; iter < 100; iter++)
            {
                double maxDelta = 0.0;
                for (int i = 0; i < n; i++)
                {
                    Complex num = EvalPoly(poly, roots[i]);
                    Complex den = Complex.One;
                    for (int j = 0; j < n; j++)
                        if (j != i) den *= (roots[i] - roots[j]);
                    if (den == Complex.Zero) continue;
                    Complex delta = num / den;
                    roots[i] -= delta;
                    double m = delta.Magnitude;
                    if (m > maxDelta) maxDelta = m;
                }
                if (maxDelta < 1e-12) break;
            }
        }

        private static Complex EvalPoly(Complex[] poly, Complex z)
        {
            Complex result = Complex.Zero;
            for (int i = 0; i < poly.Length; i++)
                result = result * z + poly[i];
            return result;
        }
    }
}