using System;

namespace Core
{
    public enum EngineMode { Live, Loop }

    public sealed class LpcEngine
    {
        private const int Order            = 20;
        private const int RingCapacity     = 64;
        private const int LoopLookahead    = 2;     // grund kö => responsiva rattar (~30 ms)
        private const int MaxRecordSeconds = 30;

        private readonly LPCAnalyzer   _analyzer;
        private readonly Synthesizer   _synth;
        private readonly Modulator     _modulator;
        private readonly FrameRing     _ring;
        private readonly FrameInfo[]   _scratch;

        private readonly FrameRecorder _recorder;
        private readonly LoopPlayer    _loopPlayer;
        private readonly FrameInfo     _loopFrame;  // förallokerat interpolationsmål

        // Lägesbyte: UI-tråden skriver _requestedMode, ljudtråden äger _mode
        // och utför själva övergången i början av ett block. Så sker Reset/Prepare
        // alltid atomiskt i förhållande till blockbearbetningen.
        private volatile EngineMode _requestedMode = EngineMode.Live;
        private EngineMode          _mode          = EngineMode.Live;   // enbart ljudtråden

        // Realtidskontroller nås härifrån (samma mönster som tidigare)
        public Modulator     Modulator  => _modulator;
        public LoopPlayer    LoopPlayer => _loopPlayer;   // Speed m.m.
        public FrameRecorder Recorder   => _recorder;     // Start/Stop/Count

        public EngineMode Mode => _mode;

        public LpcEngine(int sampleRate)
        {
            _analyzer  = new LPCAnalyzer(sampleRate, Order);
            _synth     = new Synthesizer(Order, _analyzer.Hop);   // takt = analyzer.Hop
            _modulator = new Modulator(sampleRate, Order);
            _ring      = new FrameRing(RingCapacity, Order);

            _scratch = new FrameInfo[RingCapacity];
            for (int i = 0; i < _scratch.Length; i++)
                _scratch[i] = new FrameInfo { Lpc = new float[Order] };

            // ~66,7 frames/s vid 44,1 kHz => 30 s ≈ 2000 frames ≈ 180 kB. Försumbart.
            int framesPerSecond = sampleRate / _analyzer.Hop + 1;
            _recorder   = new FrameRecorder(MaxRecordSeconds * framesPerSecond, Order);
            _loopPlayer = new LoopPlayer(_recorder);
            _loopFrame  = new FrameInfo { Lpc = new float[Order] };
        }

        // ---- styrning från UI-tråden ------------------------------------

        /// <summary>Begär loopläge. Träder i kraft vid nästa ProcessBlock.</summary>
        public void EnterLoopMode() => _requestedMode = EngineMode.Loop;

        /// <summary>Begär liveläge. Träder i kraft vid nästa ProcessBlock.</summary>
        public void EnterLiveMode() => _requestedMode = EngineMode.Live;

        // ---- ljudtråden ---------------------------------------------------

        // mono in -> mono ut, samma längd. Körs på ljudtråden, allokerar inget.
        public void ProcessBlock(ReadOnlySpan<float> input, Span<float> output)
        {
            ApplyPendingModeSwitch();

            if (_mode == EngineMode.Loop && _recorder.Count > 0)
                ProcessLoop(output);
            else
                ProcessLive(input, output);
        }

        private void ApplyPendingModeSwitch()
        {
            EngineMode requested = _requestedMode;
            if (requested == _mode) return;

            if (requested == EngineMode.Loop)
            {
                _recorder.Stop();        // pågående inspelning avslutas implicit
                _loopPlayer.Prepare();   // trimindex + nollställd läsposition.
                                         // O(antal frames) utan allokeringar — ofarligt här.
            }
            else
            {
                // Tillbaka till live: analyzerns sampelring och pre-emphasis-minne
                // innehåller inaktuellt data från före loopen. Nollställ så att
                // första live-framen inte byggs på skräp.
                _analyzer.Reset();
            }

            _mode = requested;
            // Obs: _ring töms INTE. Det som ligger där spelas klart (~max 30 ms),
            // sedan tar den nya producenten över. Skarvfritt utan lås.
        }

        private void ProcessLive(ReadOnlySpan<float> input, Span<float> output)
        {
            int n = _analyzer.Push(input, _scratch);
            for (int i = 0; i < n; i++)
            {
                _recorder.Capture(_scratch[i]);   // billig no-op när Recording == false

                if (!_ring.Full)
                {
                    _modulator.ModulateInto(_ring.WriteSlot, _scratch[i]);
                    _ring.Commit();
                }
            }
            _synth.RenderFromRing(output, _ring);
        }

        private void ProcessLoop(Span<float> output)
        {
            // Håll kön grund: moduleringen sker vid inköningen, så en djup kö
            // skulle fördröja rattarnas verkan. Lookahead 2 ≈ 30 ms.
            while (_ring.Count < LoopLookahead)
            {
                _loopPlayer.NextFrameInto(_loopFrame);                  // tempo + interpolation (rått)
                _modulator.ModulateInto(_ring.WriteSlot, _loopFrame);   // pitch + formant (färska rattlägen)
                _ring.Commit();
            }
            _synth.RenderFromRing(output, _ring);
        }
    }
}