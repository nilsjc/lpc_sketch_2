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
        _engine.LoopPlayer.Speed         = parameters.LoopSpeed;

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

    // ---- befintliga realtidskontroller --------------------------------

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

    // ---- inspelning och loop -------------------------------------------

    /// <summary>Börjar fånga analysframes. Ljudet passerar igenom som vanligt.</summary>
    public void StartRecording()
    {
        _engine.Recorder.Start();
    }

    /// <summary>Slutar fånga frames utan att byta läge.</summary>
    public void StopRecording()
    {
        _engine.Recorder.Stop();
    }

    /// <summary>Växlar till loopuppspelning av det inspelade. Avslutar
    /// implicit en pågående inspelning. Gör inget hörbart om inget spelats in.</summary>
    public void PlayLoop()
    {
        _engine.EnterLoopMode();
    }

    /// <summary>Tillbaka till live-genomströmning från mikrofonen.</summary>
    public void BackToLive()
    {
        _engine.EnterLiveMode();
    }

    /// <summary>Looptempo. 1.0 = inspelad hastighet, 0.5 = halvfart,
    /// 2.0 = dubbelfart, 0 = frusen frame, negativt = baklänges.</summary>
    public void ChangeLoopSpeed(float speed)
    {
        _engine.LoopPlayer.Speed = speed;
    }

    public bool IsRecording => _engine.Recorder.Recording;

    /// <summary>True när motorn faktiskt spelar loop (kräver att något är inspelat).</summary>
    public bool IsLooping => _engine.Mode == EngineMode.Loop && _engine.Recorder.Count > 0;

    /// <summary>Antal inspelade frames (~66,7 per sekund vid 44,1 kHz).</summary>
    public int RecordedFrames => _engine.Recorder.Count;

    public void SaveLoopToFile(string filename)
    {
        //_engine.Recorder.SaveToFile(filename);
    }
    public void LoadLoopFromFile(string filename)
    {
        //_engine.Recorder.LoadFromFile(filename);
    }
}

public class RealtimeParameters
{
    public float Pitch { get; set; }
    public float Formant { get; set; }
    public bool UseFixedPitch { get; set; }
    public int FixedPitchHz { get; set; }
    public bool UseVoicedUnvoiced { get; set; }
    public float LoopSpeed { get; set; } = 1.0f;
}