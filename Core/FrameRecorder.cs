using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Core
{
    public sealed class FrameRecorder
{
    public FrameInfo[] Frames => _frames;
    private readonly FrameInfo[] _frames;
    private int _count;
    private volatile bool _recording;

    public FrameRecorder(int maxFrames, int order)
    {
        _frames = new FrameInfo[maxFrames];
        for (int i = 0; i < maxFrames; i++)
            _frames[i] = new FrameInfo { Lpc = new float[order] };
    }

    public bool Recording => _recording;
    public int  Count     => _count;

    public void Start() { _count = 0; _recording = true; }
    public void Stop()  { _recording = false; }

    // Anropas från ljudtråden — bara kopiering, inga allokeringar
    public void Capture(FrameInfo src)
    {
        if (!_recording || _count >= _frames.Length) return;
        var dst = _frames[_count];
        Array.Copy(src.Lpc, dst.Lpc, src.Lpc.Length);
        dst.Gain        = src.Gain;
        dst.Voiced      = src.Voiced;
        dst.PitchPeriod = src.PitchPeriod;
        _count++;
    }
}
}