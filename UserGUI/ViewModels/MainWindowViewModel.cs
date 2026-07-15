// https://docs.avaloniaui.net/docs/data-binding/binding-to-commands

using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core;
namespace UserGUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public RealtimeClass Host { get; } = new RealtimeClass();
    private bool _isHostRun { get; set; } = false;
    private bool _fixedPitch { get; set; } = false;
    private bool _voiceUnvoiced { get; set; } = true;

    [ObservableProperty]
    private double _pitch;

    [ObservableProperty]
    private double formant;

    [ObservableProperty]
    private double sliderValue_3;

    private double _loopSpeedValue = 100.0;
    private double _pitchSliderValue = 12.0;
    private double _formantSliderValue = 100.0;

    public double LoopSpeedValue
    {
        get => _loopSpeedValue;
        set
        {
            if (_loopSpeedValue != value)
            {
                _loopSpeedValue = value;
                OnPropertyChanged();
                OnLoopSpeedValueChanged(value);
            }
        }
    }

    public double PitchSliderValue
    {
        get => _pitchSliderValue;
        set
        {
            if (_pitchSliderValue != value)
            {
                _pitchSliderValue = value;
                OnPropertyChanged();
                OnPitchSliderValueChanged(value); // Call your function here
            }
        }
    }

    public double FormantSliderValue
    {
        get => _formantSliderValue;
        set
        {
            if (_formantSliderValue != value)
            {
                _formantSliderValue = value;
                OnPropertyChanged();
                OnFormantSliderValueChanged(value); // Call your function here
            }
        }
    }

    [RelayCommand]
    public void OnRobot()
    {
        if (_fixedPitch)
        {
            _fixedPitch = false;
        }
        else
        {
            _fixedPitch = true;
        }
        Host.Robot(_fixedPitch);
    }

    [RelayCommand]
    public void OnVoiceUnvoiced()
    {
        if(_voiceUnvoiced)
        {
            _voiceUnvoiced = false;
        }
        else
        {
            _voiceUnvoiced = true;
        }
        Host.VoiceUnvoiced(_voiceUnvoiced);
    }

    [RelayCommand]
    public void StartStopEngine()
    {
        if (_isHostRun)
        {
            _isHostRun = false;
            Host.Stop();
        }
        else
        {
            _isHostRun = true;
            var setPitchFreq = (float)(_pitchSliderValue - 12);
            var setFormant = (float)(_formantSliderValue/100.0);
            Host.Run(new RealtimeParameters
            {
                Pitch = setPitchFreq,
                Formant = setFormant,
                UseFixedPitch = _fixedPitch,
                UseVoicedUnvoiced = _voiceUnvoiced,
                FixedPitchHz = 60
            });
        }
    }

    [RelayCommand]
    public void OnStartStopRecording()
    {
        if (Host.IsRecording)
        {
            Host.StopRecording();
        }
        else
        {
            Host.StartRecording();
        }
    }

    [RelayCommand]
    public void OnPlayRecording()
    {
        if (Host.IsLooping)
        {
            Host.BackToLive();
        }
        else
        {
            Host.PlayLoop();
        }
        
    }

    public void OnLoopSpeedValueChanged(double value)
    {
        float result = (float)(value);
        result /= 25.0f;
        result -= 2.0f;
        Host.ChangeLoopSpeed(result);
    }

    public void OnPitchSliderValueChanged(double value)
    {
        float result = (float)(value - 12);
        Host.ChangePitch(result);
    }

    public void OnFormantSliderValueChanged(double value)
    {
        float result = (float)(value/100.0);
        Host.ChangeFormant(result);
    }
}
