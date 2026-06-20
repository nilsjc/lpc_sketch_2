using System;
using PortAudioSharp;
using Core;

public class RealtimeClass
{
    LpcEngine _engine;
    int _sampleRate = 44100;
    PortAudioSharp.Stream _stream;
    public RealtimeClass(int sampleRate = 44100)
    {
        _sampleRate = sampleRate;
    }
    public void Run(RealtimeParameters parameters)
    {
        _engine = new LpcEngine(_sampleRate);
        _engine.Modulator.UseFixedPitch  = parameters.UseFixedPitch;
        _engine.Modulator.PitchSemitones = parameters.Pitch;
        _engine.Modulator.FormantScale   = parameters.Formant;
        _engine.Modulator.FixedPitchHz   = parameters.FixedPitchHz;
        const uint framesPerBuffer = 256;   // låg latens; justera vid behov
        
        PortAudio.Initialize();

        var inParams = new StreamParameters
        {
            device           = PortAudio.DefaultInputDevice,
            channelCount     = 1,
            sampleFormat     = SampleFormat.Float32,
            suggestedLatency = PortAudio.GetDeviceInfo(PortAudio.DefaultInputDevice).defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };
        var outParams = new StreamParameters
        {
            device           = PortAudio.DefaultOutputDevice,
            channelCount     = 1,
            sampleFormat     = SampleFormat.Float32,
            suggestedLatency = PortAudio.GetDeviceInfo(PortAudio.DefaultOutputDevice).defaultLowOutputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        PortAudioSharp.Stream.Callback callback =
            (IntPtr input, IntPtr output, uint frameCount,
             ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData) =>
        {
            unsafe
            {
                var inSpan  = new ReadOnlySpan<float>((void*)input,  (int)frameCount);
                var outSpan = new Span<float>((void*)output, (int)frameCount);
                _engine.ProcessBlock(inSpan, outSpan);
            }
            return StreamCallbackResult.Continue;
        };

        _stream = new PortAudioSharp.Stream(
            inParams, outParams, _sampleRate, framesPerBuffer,
            StreamFlags.ClipOff, callback, IntPtr.Zero);

        _stream.Start();
    }
    public void Stop()
    {
        _stream.Stop();
        _stream.Dispose();
        PortAudio.Terminate();
    }
    public void Robot(bool fixedPitch)
    {
        _engine.Modulator.UseFixedPitch = fixedPitch;
    }
    public void VoiceUnvoiced(bool voiceUnvoiced)
    {
        _engine.Modulator.VoiceUnvoiced = voiceUnvoiced;
    }

    public void ChangePitch(float pitch)
    {
        _engine.Modulator.PitchSemitones = pitch;
    }

    public void ChangeFormant(float formant)
    {
        _engine.Modulator.FormantScale = formant;
    }
}
public class RealtimeParameters
{
    public float Pitch { get; set; }
    public float Formant { get; set; }
    public bool UseFixedPitch { get; set; }
    public int FixedPitchHz { get; set; }
    public bool UseVoicedUnvoiced { get; set; }

}