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

    private double _pitchSliderValue;
    private double _formantSliderValue;

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
            Host.Run(new RealtimeParameters
            {
                Pitch = _pitchSliderValue,
                Formant = _formantSliderValue,
                UseFixedPitch = _fixedPitch,
                UseVoicedUnvoiced = _voiceUnvoiced
            });
        }
    }

    public void OnPitchSliderValueChanged(double value)
    {
        // Implementation for slider value change
        Host.ChangePitch(value);
    }

    public void OnFormantSliderValueChanged(double value)
    {
        // Implementation for formant slider value change
        Host.ChangeFormant(value);
    }

    [RelayCommand]
    public void OnChangeFormant()
    {
        // Implementation for Change Formant command
        Host.ChangeFormant(Formant);
    }
}
