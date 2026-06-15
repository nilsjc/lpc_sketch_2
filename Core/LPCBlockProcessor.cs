// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Threading.Tasks;
// using System.Numerics;
// using SoundFlow.Abstracts;

// namespace Core
// {
//     public class LPCBlockProcessor : SoundModifier
//     {
//         private const int Order = 20;

//         private readonly LPCAnalyzer _analyzer;
//         private readonly Synthesizer    _synth;
//         private readonly FrameRing   _ring;
//         private readonly FrameInfo[] _scratch;   // förallokerade analysramar
//         private float[] _mono;                    // mono in/ut-buffert
//         private Modulator _modulator;
//         public Modulator Modulator => _modulator;

//         private readonly int _sampleRate;


//         public LPCBlockProcessor(int sampleRate)
//         {
//             _analyzer = new LPCAnalyzer(sampleRate, Order);
//             _synth    = new Synthesizer(Order, _analyzer.Hop);   // <-- takt kopplad till analysatorn
//             _ring     = new FrameRing(capacity: 32, order: Order);

//             _scratch = new FrameInfo[16];
//             for (int i = 0; i < _scratch.Length; i++)
//                 _scratch[i] = new FrameInfo { Lpc = new float[Order] };

//             _mono = new float[2048];
//             _sampleRate = sampleRate;
//             _modulator = new(sampleRate, Order);
//         }

//         public override void Process(Span<float> buffer, int channels)
//         {
//             int frames = buffer.Length / channels;                  // härleds ur bufferten

//             if (_mono.Length < frames) _mono = new float[frames];   // växer max en gång
//             var mono = _mono.AsSpan(0, frames);

//             // 1. nermix till mono
//             for (int f = 0; f < frames; f++)
//             {
//                 float s = 0f;
//                 for (int c = 0; c < channels; c++) s += buffer[f * channels + c];
//                 mono[f] = s / channels;
//             }

//             // 2. analysera -> modulera -> lägg i ringen
//             int n = _analyzer.Push(mono, _scratch);
//             for (int i = 0; i < n && !_ring.Full; i++)
//             {
//                 _modulator.ModulateInto(_ring.WriteSlot, _scratch[i]);   // delegera
//                 _ring.Commit();
//             }

//             // 3. rendera lika många samples tillbaka
//             _synth.RenderFromRing(mono, _ring);

//             // 4. upmix mono -> alla kanaler
//             for (int f = 0; f < frames; f++)
//                 for (int c = 0; c < channels; c++)
//                     buffer[f * channels + c] = mono[f];
//         }

//         public override float ProcessSample(float sample, int channel)
//         {
//             return sample;
//         }

//     }
// }