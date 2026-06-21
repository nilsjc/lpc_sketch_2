using System;

namespace Core
{
    public sealed class LpcEngine
    {
        private const int Order = 20;

        private readonly LPCAnalyzer _analyzer;
        private readonly Synthesizer _synth;
        private readonly Modulator   _modulator;
        private readonly FrameRing   _ring;
        private readonly FrameInfo[] _scratch;

        public Modulator Modulator => _modulator;   // realtidskontroller nås härifrån

        public LpcEngine(int sampleRate)
        {
            _analyzer  = new LPCAnalyzer(sampleRate, Order);
            _synth     = new Synthesizer(Order, _analyzer.Hop);   // takt = analyzer.Hop
            _modulator = new Modulator(sampleRate, Order);
            _ring      = new FrameRing(capacity: 64, order: Order);

            _scratch = new FrameInfo[64];
            for (int i = 0; i < _scratch.Length; i++)
                _scratch[i] = new FrameInfo { Lpc = new float[Order] };
        }

        // mono in -> mono ut, samma längd. Körs på ljudtråden, allokerar inget.
        public void ProcessBlock(ReadOnlySpan<float> input, Span<float> output)
        {
            int n = _analyzer.Push(input, _scratch);
            for (int i = 0; i < n && !_ring.Full; i++)
            {
                _modulator.ModulateInto(_ring.WriteSlot, _scratch[i]);
                _ring.Commit();
            }
            _synth.RenderFromRing(output, _ring);
        }
    }
}