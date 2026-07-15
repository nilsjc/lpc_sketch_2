namespace Core
{
    // Synten och analysatorn måste vara kopplade i takt: synten ska emittera 
    // precis analyzer.Hop samples per frame. Skapa alltså synten med 
    // samplesPerFrame = analyzer.Hop, annars glider tids­basen isär.
    
    public class LPCAnalyzer
    {
        private const float PreEmphasis      = 0.97f;
        private const float VoicingThreshold = 0.30f;

        private readonly int _sampleRate;
        private readonly int _order;
        private readonly int _frameLength;
        private readonly int _hop;

        // återanvänd scratch (allokeras en gång, inte per frame)
        private readonly float[] _ring;       // de senaste _frameLength samplen
        private readonly float[] _work;       // fönstrad frame inför analys
        private readonly float[] _lpc;        // lpcCoeffs[order+1]
        private readonly float[] _autocorr;   // autocorr[order+1]
        private readonly float[] _reflect;    // reflectionCoeffs[order+1]

        // --- bestående tillstånd ---
        private int   _writePos;     // skrivpekare i ringbufferten
        private int   _countdown;    // samples kvar till nästa frame
        private float _preEmphPrev;  // pre-emphasis-filtrets minne (kontinuerligt)

        public LPCAnalyzer(int sampleRate, int order = 20, float frameSeconds = 0.030f)
        {
            _sampleRate  = sampleRate;
            _order       = order;
            _frameLength = (int)(frameSeconds * sampleRate);
            _hop         = _frameLength / 2;   // 50% överlappning

            _ring      = new float[_frameLength];
            _work      = new float[_frameLength];
            _lpc       = new float[order + 1];
            _autocorr  = new float[order + 1];
            _reflect   = new float[order + 1];
            _countdown = _frameLength;
        }

        /// <summary>Synten måste emittera exakt så här många samples per frame.</summary>
        public int Hop => _hop;

        /// <summary>
        /// Matar in samples; färdiga frames skrivs till 'frames' allteftersom de bildas.
        /// Returnerar antal frames som skrevs (ofta 0, ibland 1 per block).
        /// Storleksätt 'frames' till minst input.Length / Hop + 1.
        /// </summary>
        public int Push(ReadOnlySpan<float> input, Span<FrameInfo> frames)
        {
            int produced = 0;

            for (int n = 0; n < input.Length; n++)
            {
                // pre-emphasis, kontinuerlig över blockgränserna: y[n] = x[n] - a*x[n-1]
                float x = input[n] - PreEmphasis * _preEmphPrev;
                _preEmphPrev = input[n];

                _ring[_writePos] = x;
                _writePos = (_writePos + 1) % _frameLength;
                _countdown--;

                if (_countdown == 0)
                {
                    if (produced < frames.Length)
                        BuildFrameInto(frames[produced++]);
                    _countdown = _hop;
                }
            }

            return produced;
        }

        public void Reset()
        {
            _writePos    = 0;
            _countdown   = _frameLength;
            _preEmphPrev = 0f;
            Array.Clear(_ring, 0, _ring.Length);
        }

        // DSP calculations

        private float CalculateHanning(int index, int length)
            => (float)(0.5 * (1 - Math.Cos((2 * Math.PI * index) / (length - 1))));

        private static float PerformLPCAnalysis(float[] frame, int order,
            float[] lpcCoeffs, float[] autocorr, float[] reflectionCoeffs)
        {
            int frameSize = frame.Length;

            for (int k = 0; k <= order; k++)
            {
                float sum = 0.0f;
                for (int i = 0; i < frameSize - k; i++)
                    sum += frame[i] * frame[i + k];
                autocorr[k] = sum;
            }

            lpcCoeffs[0] = 1.0f;
            float err = autocorr[0];
            if (err <= 0.0f)
            {
                for (int i = 0; i <= order; i++) lpcCoeffs[i] = 0.0f;
                return 0.0f;
            }

            reflectionCoeffs[0] = -autocorr[1] / autocorr[0];
            lpcCoeffs[1] = reflectionCoeffs[0];
            err *= (1.0f - reflectionCoeffs[0] * reflectionCoeffs[0]);

            for (int m = 1; m < order; m++)
            {
                float sum = 0.0f;
                for (int j = 0; j <= m; j++)
                    sum += lpcCoeffs[j] * autocorr[m + 1 - j];

                reflectionCoeffs[m] = (err != 0.0f) ? -sum / err : 0.0f;

                for (int j = 1; j <= (m + 1) / 2; j++)
                {
                    float tmp = lpcCoeffs[j] + reflectionCoeffs[m] * lpcCoeffs[m + 1 - j];
                    lpcCoeffs[m + 1 - j] += reflectionCoeffs[m] * lpcCoeffs[j];
                    lpcCoeffs[j] = tmp;
                }
                lpcCoeffs[m + 1] = reflectionCoeffs[m];
                err *= (1.0f - reflectionCoeffs[m] * reflectionCoeffs[m]);
            }

            return err;
        }

        private void EstimatePitch(float[] frame, out bool voiced, out int period)
        {
            int minLag = Math.Max(1, _sampleRate / 350);
            int maxLag = Math.Min(frame.Length - 1, _sampleRate / 70);

            float r0 = 0f;
            for (int n = 0; n < frame.Length; n++) r0 += frame[n] * frame[n];

            voiced = false;
            period = _sampleRate / 120;          // fallback ~120 Hz
            if (r0 <= 0f) return;

            float best = 0f; int bestLag = -1;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                float sum = 0f;
                for (int n = 0; n < frame.Length - lag; n++)
                    sum += frame[n] * frame[n + lag];
                float norm = sum / r0;
                if (norm > best) { best = norm; bestLag = lag; }
            }

            if (best >= VoicingThreshold && bestLag > 0) { voiced = true; period = bestLag; }
        }
        private void BuildFrameInto(FrameInfo info)   // info.Lpc redan allokerad till _order
        {
            for (int k = 0; k < _frameLength; k++)
                _work[k] = _ring[(_writePos + k) % _frameLength] * CalculateHanning(k, _frameLength);

            float error = PerformLPCAnalysis(_work, _order, _lpc, _autocorr, _reflect);
            info.Gain = (float)Math.Sqrt(Math.Max(error, 0f) / _frameLength);
            Array.Copy(_lpc, 1, info.Lpc, 0, _order);          // in i befintlig buffert

            // bandbreddsexpansion: a_k *= gamma^k -> alla poler en gnutta inåt
            const float gamma = 0.996f;
            float g = gamma;
            for (int i = 0; i < _order; i++) { info.Lpc[i] *= g; g *= gamma; }

            EstimatePitch(_work, out bool voiced, out int period);
            info.Voiced = voiced;
            info.PitchPeriod = period;
        }  
    }
}