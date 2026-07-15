using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Core
{
    public sealed class LoopPlayer
{
    private readonly FrameRecorder _rec;
    private float _pos;
    private int _start, _end;              // trimindex (sätts vid Prepare)
    private const float TrimThresholdRatio = 0.05f;  // 5 % av inspelningens maxgain
    private const int   PadFrames          = 2;      // ~30 ms luft runt talet
    private const int   MinLoopFrames      = 8;      // ~120 ms minsta meningsfulla loop

    public float Speed { get; set; } = 1f; // sätts från UI, läses på ljudtråden

    public LoopPlayer(FrameRecorder rec) => _rec = rec;

    public void Prepare()                  // anropas efter rec.Stop()
    {
        _pos = 0f;
        (_start, _end) = TrimSilence();    // gain-tröskel, som vi pratade om
    }

    public void NextFrameInto(FrameInfo dst)
    {
        int len = _end - _start;
        int i0 = _start + (int)_pos;
        int i1 = _start + ((int)_pos + 1) % len;
        float t = _pos - (int)_pos;

        InterpolateInto(dst, _rec.Frames[i0], _rec.Frames[i1], t);

        _pos += Speed;
        while (_pos >= len) _pos -= len;
        while (_pos < 0)    _pos += len;
    }

        private void InterpolateInto(FrameInfo dst, FrameInfo frameInfo1, FrameInfo frameInfo2, float t)
        {
            // t = 0 -> exakt frameInfo1, t = 1 -> exakt frameInfo2

            // Gain: linjärt, oproblematiskt
            dst.Gain = frameInfo1.Gain + t * (frameInfo2.Gain - frameInfo1.Gain);

            // Voiced/pitch: interpolera perioden bara när BÅDA är tonande.
            // Över en tonande/tonlös-gräns är den tonlösa framens period bara
            // ett fallback-värde — att blanda in det ger pitchglitchar.
            if (frameInfo1.Voiced && frameInfo2.Voiced)
            {
                dst.Voiced = true;
                dst.PitchPeriod = Math.Max(1, (int)Math.Round(
                    frameInfo1.PitchPeriod + t * (frameInfo2.PitchPeriod - frameInfo1.PitchPeriod)));
            }
            else
            {
                // binärt: ta närmaste framen rakt av
                FrameInfo nearest = (t < 0.5f) ? frameInfo1 : frameInfo2;
                dst.Voiced      = nearest.Voiced;
                dst.PitchPeriod = nearest.PitchPeriod;
            }

            // LPC-koefficienter: rak linjärinterpolation.
            // Teoretiskt inte stabilitetsgaranterat, men i praktiken snällt här:
            // bandbreddsexpansionen (gamma 0.996) har redan dragit polerna inåt
            // och grannframes delar halva analysfönstret. Hörs "blipp" vid snabba
            // klangväxlingar är rätt åtgärd att gå via reflektionskoefficienter.
            for (int k = 0; k < dst.Lpc.Length; k++)
                dst.Lpc[k] = frameInfo1.Lpc[k] + t * (frameInfo2.Lpc[k] - frameInfo1.Lpc[k]);
        }

        // i LoopPlayer

        private (int start, int end) TrimSilence()
    {
        int count = _rec.Count;
        if (count == 0) return (0, 0);

        // Svep 1: hitta maxgain — referensen för den relativa tröskeln
        float maxGain = 0f;
        for (int i = 0; i < count; i++)
            if (_rec.Frames[i].Gain > maxGain) maxGain = _rec.Frames[i].Gain;

        if (maxGain <= 0f) return (0, count);   // helt tyst inspelning: ta allt

        float threshold = maxGain * TrimThresholdRatio;

        // Svep 2: framifrån tills första framen över tröskeln
        int start = 0;
        while (start < count && _rec.Frames[start].Gain < threshold) start++;

        // Svep 3: bakifrån tills sista framen över tröskeln
        int end = count;
        while (end > start && _rec.Frames[end - 1].Gain < threshold) end--;

        // lite luft så talets ansats och utklinga inte kapas
        start = Math.Max(0, start - PadFrames);
        end   = Math.Min(count, end + PadFrames);

        // för lite kvar? Trimningen har misslyckats — fall tillbaka till hela inspelningen
        if (end - start < MinLoopFrames) return (0, count);

        return (start, end);
    }
    }
}