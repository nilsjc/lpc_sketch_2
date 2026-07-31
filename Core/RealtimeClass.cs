using System;
using System.Collections.Generic;
using PortAudioSharp;
using Core;

public class RealtimeClass
{
    // ---- PortAudio-livscykel (processglobal) ---------------------------
    //
    // Initialize/Terminate hör till programmets livscykel, inte till en
    // enskild stream. Anropa InitializeAudio() en gång vid uppstart och
    // ShutdownAudio() en gång vid avslut.

    private static bool _paInitialized;

    public static void InitializeAudio()
    {
        if (_paInitialized) return;
        PortAudio.LoadNativeLibrary();
        PortAudio.Initialize();          // kastar PortAudioException vid fel
        _paInitialized = true;
    }

    public static void ShutdownAudio()
    {
        if (!_paInitialized) return;
        PortAudio.Terminate();
        _paInitialized = false;
    }

    /// <summary>Stäng och öppna om PortAudio för att plocka upp enheter som
    /// kopplats in eller ur. Alla strömmar måste vara stoppade först.</summary>
    public static void RefreshDevices()
    {
        ShutdownAudio();
        InitializeAudio();
    }

    private static void EnsureInitialized()
    {
        if (!_paInitialized)
            throw new InvalidOperationException(
                "RealtimeClass.InitializeAudio() måste anropas innan enheter listas eller en stream startas.");
    }

    private static int CheckedDeviceCount()
    {
        int n = PortAudio.DeviceCount;
        if (n < 0)
            throw new InvalidOperationException(
                $"PortAudio.DeviceCount misslyckades: {(ErrorCode)n} — {PortAudio.GetErrorText((ErrorCode)n)}");
        return n;
    }

    // ---- enhetslistning -------------------------------------------------

    /// <summary>Enhet plus dess index. Indexet är det som skickas till
    /// SetInputDevice/SetOutputDevice — DeviceInfo bär inte med sig det.</summary>
    public readonly struct AudioDevice
    {
        public int Index { get; }
        public DeviceInfo Info { get; }

        public AudioDevice(int index, DeviceInfo info)
        {
            Index = index;
            Info  = info;
        }

        public string Name => Info.name;

        public override string ToString() => $"[{Index}] {Info.name}";
    }

    public static Device[] GetInputDevices()  => GetDevices(wantInput: true);
    public static Device[] GetOutputDevices() => GetDevices(wantInput: false);

    private static Device[] GetDevices(bool wantInput)
    {
        EnsureInitialized();

        int count = CheckedDeviceCount();
        var result = new List<AudioDevice>(count);

        for (int i = 0; i < count; i++)
        {
            DeviceInfo info = PortAudio.GetDeviceInfo(i);
            int channels = wantInput ? info.maxInputChannels : info.maxOutputChannels;
            if (channels > 0)
                result.Add(new AudioDevice(i, info));
        }
        var devices = new List<Device>();
        foreach (var device in result)
        {
            devices.Add(new Device { Name = device.Name, Id = device.Index });
        }
        return [.. devices];
    }

    // ---- instans --------------------------------------------------------

    private const int Channels = 1;             // mono in och ut

    private LpcEngine _engine;
    private readonly int _sampleRate;
    private PortAudioSharp.Stream _stream;

    // Måste hållas vid liv så länge strömmen lever: native-sidan har bara
    // en funktionspekare, som GC inte ser.
    private PortAudioSharp.Stream.Callback _callback;

    private int _inputDeviceIndex  = PortAudio.NoDevice;   // -1 = inte valt
    private int _outputDeviceIndex = PortAudio.NoDevice;

    public RealtimeClass(int sampleRate = 44100)
    {
        _sampleRate = sampleRate;
    }

    public void SetInputDevice(int deviceIndex)
    {
        EnsureInitialized();
        if (deviceIndex < 0 || deviceIndex >= CheckedDeviceCount())
            throw new ArgumentOutOfRangeException(nameof(deviceIndex), "Ogiltigt enhetsindex.");
        if (PortAudio.GetDeviceInfo(deviceIndex).maxInputChannels < Channels)
            throw new ArgumentException("Enheten har inga ingångskanaler.", nameof(deviceIndex));

        _inputDeviceIndex = deviceIndex;
    }

    public void SetOutputDevice(int deviceIndex)
    {
        EnsureInitialized();
        if (deviceIndex < 0 || deviceIndex >= CheckedDeviceCount())
            throw new ArgumentOutOfRangeException(nameof(deviceIndex), "Ogiltigt enhetsindex.");
        if (PortAudio.GetDeviceInfo(deviceIndex).maxOutputChannels < Channels)
            throw new ArgumentException("Enheten har inga utgångskanaler.", nameof(deviceIndex));

        _outputDeviceIndex = deviceIndex;
    }

    public void Run(RealtimeParameters parameters)
    {
        EnsureInitialized();

        if (_stream != null)
            throw new InvalidOperationException("Strömmen är redan igång. Anropa Stop() först.");

        // Faller tillbaka på systemets default bara om inget valts explicit.
        if (_inputDeviceIndex  == PortAudio.NoDevice) _inputDeviceIndex  = PortAudio.DefaultInputDevice;
        if (_outputDeviceIndex == PortAudio.NoDevice) _outputDeviceIndex = PortAudio.DefaultOutputDevice;

        if (_inputDeviceIndex == PortAudio.NoDevice)
            throw new InvalidOperationException("Ingen ingångsenhet tillgänglig.");
        if (_outputDeviceIndex == PortAudio.NoDevice)
            throw new InvalidOperationException("Ingen utgångsenhet tillgänglig.");

        DeviceInfo inInfo  = PortAudio.GetDeviceInfo(_inputDeviceIndex);
        DeviceInfo outInfo = PortAudio.GetDeviceInfo(_outputDeviceIndex);

        _engine = new LpcEngine(_sampleRate);
        _engine.Modulator.UseFixedPitch  = parameters.UseFixedPitch;
        _engine.Modulator.PitchSemitones = parameters.Pitch;
        _engine.Modulator.FormantScale   = parameters.Formant;
        _engine.Modulator.FixedPitchHz   = parameters.FixedPitchHz;
        _engine.LoopPlayer.Speed         = parameters.LoopSpeed;

        const uint framesPerBuffer = 256;   // låg latens; justera vid behov

        var inParams = new StreamParameters
        {
            device                    = _inputDeviceIndex,
            channelCount              = Channels,
            sampleFormat              = SampleFormat.Float32,
            suggestedLatency          = inInfo.defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        var outParams = new StreamParameters
        {
            device                    = _outputDeviceIndex,
            channelCount              = Channels,
            sampleFormat              = SampleFormat.Float32,
            suggestedLatency          = outInfo.defaultLowOutputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        _callback =
            (IntPtr input, IntPtr output, uint frameCount,
             ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData) =>
        {
            // Ingenting får kastas härifrån — anropet kommer från native-kod.
            try
            {
                unsafe
                {
                    int samples = (int)frameCount * Channels;
                    var outSpan = new Span<float>((void*)output, samples);

                    if (input == IntPtr.Zero)
                    {
                        outSpan.Clear();
                        return StreamCallbackResult.Continue;
                    }

                    var inSpan = new ReadOnlySpan<float>((void*)input, samples);
                    _engine.ProcessBlock(inSpan, outSpan);
                }
            }
            catch
            {
                unsafe
                {
                    new Span<float>((void*)output, (int)frameCount * Channels).Clear();
                }
            }

            return StreamCallbackResult.Continue;
        };

        // PortAudioSharp2 saknar Pa_IsFormatSupported, så enda sättet att veta
        // om kombinationen fungerar är att försöka öppna strömmen.
        try
        {
            _stream = new PortAudioSharp.Stream(
                inParams, outParams, _sampleRate, framesPerBuffer,
                StreamFlags.ClipOff, _callback, IntPtr.Zero);

            _stream.Start();
        }
        catch
        {
            _stream?.Dispose();
            _stream = null;
            _callback = null;
            throw;
        }
    }

    public void Stop()
    {
        if (_stream == null) return;

        try
        {
            _stream.Stop();
        }
        finally
        {
            _stream.Dispose();
            _stream = null;
            _callback = null;      // först nu är delegaten säker att släppa
        }
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