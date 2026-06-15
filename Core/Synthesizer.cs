using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Core
{
    public class Synthesizer
    {
        private const float PreEmphasis = 0.97f;
        private readonly int     _order;
        private readonly int     _samplesPerFrame;     // = hop
        private readonly float[] _bp;                   // filterhistorik (ringbuffert)
        private readonly Random  _rand = new(1234);

        private float _dcX1, _dcY1;   // enkel DC-blocker för att hålla utspänningen i schack

        // --- bestående tillstånd, lever över anropen ---
        private int   _offset;          // filtrets skrivpekare (snurrar per sampel)
        private int   _pulseCountdown;  // samples till nästa glottispuls
        private float _deEmphPrev;      // de-emphasis-filtrets minne
        private int   _frameCursor;     // vilken ram i strömmen
        private int   _posInFrame;      // position inom den ramen
        private readonly FrameParams _current;   // = new() { Lpc = new float[order] }, förallokerad
        private bool _have;                       // har vi fått minst en ram?

        public Synthesizer(int order, int samplesPerFrame)
        {
            _order = order;
            _samplesPerFrame = samplesPerFrame;
            _bp = new float[order];
            _current = new FrameParams { Lpc = new float[order] };
        }

        /// <summary>
        /// Fyller 'output' med syntetiserat ljud, drar ramar ur 'frames' allteftersom.
        /// Returnerar antal skrivna samples (mindre än output.Length, eller 0, vid slut).
        /// </summary>
        public int Render(Span<float> output, ReadOnlySpan<FrameParams> frames)
        {
            int written = 0;

            for (int i = 0; i < output.Length; i++)
            {
                if (_frameCursor >= frames.Length)
                    break;                          // slut på ramar -> avsluta tidigt

                FrameParams fp = frames[_frameCursor];

                // audio rate: ett excitationssampel genom all-pol-filtret
                float e = NextExcitation(fp);
                float y = AllPoleFilter(e, fp.Lpc);

                // streamande de-emphasis: y[n] = x[n] + a*y[n-1]
                y += PreEmphasis * _deEmphPrev;
                _deEmphPrev = y;

                output[i] = y;
                written++;

                // stega positionen inom ramen; växla ram när den är fylld
                if (++_posInFrame >= _samplesPerFrame)
                {
                    _posInFrame = 0;
                    _frameCursor++;
                }
            }

            return written;
        }

        private float NextExcitation(in FrameParams fp)
        {
            if (!fp.Voiced)
                return fp.Gain * NextGaussian(_rand);

            float e = 0f;
            if (_pulseCountdown <= 0)
            {
                e = fp.Gain * (float)Math.Sqrt(fp.Period);
                _pulseCountdown = fp.Period;        // använd nuvarande rams period
            }
            _pulseCountdown--;
            return e;
        }

        private float AllPoleFilter(float e, float[] co)
        {
            float sum = e;
            for (int j = 0; j < _order; j++)
            {
                int idx = (_offset + _order - j) % _order;
                sum -= co[j] * _bp[idx];
            }
            _offset = (_offset + 1) % _order;
            _bp[_offset] = sum;
            return sum;
        }

        private static float NextGaussian(Random rand)
        {
            double u1 = 1.0 - rand.NextDouble();
            double u2 = 1.0 - rand.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        public void RenderFromRing(Span<float> output, FrameRing ring)
        {
            for (int i = 0; i < output.Length; i++)
            {
                if (_posInFrame == 0 && !ring.Empty)   // dags för ny ram och en finns
                {
                    CopyInto(_current, ring.Read());
                    _have = true;
                }

                if (!_have) { output[i] = 0f; continue; }   // priming: inget att spela än

                float e = NextExcitation(_current);
                float y = AllPoleFilter(e, _current.Lpc);
                y += PreEmphasis * _deEmphPrev;
                _deEmphPrev = y;

                if (!float.IsFinite(y))     // filtret blåste upp -> återställ istället för att dö
                {
                    Console.Write("E1 ");
                    // y = 0f;
                    // Array.Clear(_bp, 0, _bp.Length);
                    // _deEmphPrev = 0f;
                    // _pulseCountdown = 0;
                }
                float dc = y - _dcX1 + 0.995f * _dcY1;
                _dcX1 = y;
                _dcY1 = dc;

                output[i] = Math.Clamp(dc, -1f, 1f); 

                if (++_posInFrame >= _samplesPerFrame) _posInFrame = 0;
            }
        }

        private static void CopyInto(FrameParams dst, FrameParams src)
        {
            dst.Voiced = src.Voiced;
            dst.Gain   = src.Gain;
            dst.Period = src.Period;
            Array.Copy(src.Lpc, dst.Lpc, src.Lpc.Length);   // egen kopia -> ringplatsen blir fri
        }

        /// <summary>Nollställer allt löpande tillstånd inför en ny rendering.</summary>
        public void Reset()
        {
            _frameCursor    = 0;
            _posInFrame     = 0;
            _offset         = 0;
            _pulseCountdown = 0;
            _deEmphPrev     = 0f;
            Array.Clear(_bp, 0, _bp.Length);   // töm filterhistoriken
        }
    }
}