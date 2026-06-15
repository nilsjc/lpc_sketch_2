using System;
using PortAudioSharp;
using Core;

public static class RealtimeHost
{
    public static void Run()
    {
        const int  sampleRate     = 44100;
        const uint framesPerBuffer = 256;   // låg latens; justera vid behov
        Console.WriteLine("use fixed pitch y/n ?");
        bool useFixedPitch = Console.ReadLine()?.Trim().ToLower() == "y"; 
        Console.WriteLine("pitch in semitones:");
        float pitchSemitones = float.Parse(Console.ReadLine());

        PortAudio.Initialize();

        var engine = new LpcEngine(sampleRate);
        // exempel på rattar:
        engine.Modulator.UseFixedPitch  = useFixedPitch;
        engine.Modulator.PitchSemitones = pitchSemitones;
        engine.Modulator.FormantScale   = 1.0f;
        engine.Modulator.FixedPitchHz   = 40;

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
                engine.ProcessBlock(inSpan, outSpan);
            }
            return StreamCallbackResult.Continue;
        };

        var stream = new PortAudioSharp.Stream(
            inParams, outParams, sampleRate, framesPerBuffer,
            StreamFlags.ClipOff, callback, IntPtr.Zero);

        stream.Start();
        Console.WriteLine("Realtid igång. Tryck Enter för att stoppa.");
        Console.ReadLine();
        stream.Stop();
        stream.Dispose();
        PortAudio.Terminate();
    }
}