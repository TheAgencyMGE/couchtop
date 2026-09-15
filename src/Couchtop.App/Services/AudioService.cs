using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Couchtop.Core.Audio;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Settings;

namespace Couchtop.App.Services;

/// <summary>
/// Low-latency mixer for effects and the ambience loop. The output device is opened only while something is
/// audible and closed again when idle, so Couchtop uses no audio CPU while an app is in front.
/// Users can replace the menu music and menu sounds with their own files.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly UserSettings _settings;
    private readonly CustomAudioLibrary? _library;
    private readonly object _gate = new();
    private readonly WaveFormat _format = CustomAudioLoader.Format;
    private readonly MixingSampleProvider _mixer;
    private readonly Timer _idleTimer;
    private readonly Dictionary<SoundEffect, float[]> _custom = new();
    private WaveOutEvent? _output;
    private SoundBank? _bank;
    private LoopingFileSource? _customMusic;
    private AmbienceProvider? _ambience;
    private bool _ambienceWanted;
    private DateTime _lastActivity = DateTime.UtcNow;
    private readonly Dictionary<SoundEffect, DateTime> _lastPlayed = new();

    public AudioService(UserSettings settings, CustomAudioLibrary? library = null)
    {
        _settings = settings;
        _library = library;
        _mixer = new MixingSampleProvider(_format) { ReadFully = true };
        _idleTimer = new Timer(_ => CheckIdle(), null, 2000, 2000);
    }

    public bool IsReady => _bank is not null;

    public Task InitializeAsync() => Task.Run(() =>
    {
        try
        {
            var bank = SoundSynth.Create();
            lock (_gate) _bank = bank;
        }
        catch (Exception ex)
        {
            Log.Error("Sound synthesis failed", ex);
        }
        LoadCustomAudio();
        if (_ambienceWanted) SetAmbience(true);
    });

    /// <summary>Re-reads the custom music and sounds from settings (after the user changed them).</summary>
    public Task ReloadCustomAudioAsync() => Task.Run(() =>
    {
        LoadCustomAudio();
        SetAmbience(_ambienceWanted);
    });

    private void LoadCustomAudio()
    {
        var clips = new Dictionary<SoundEffect, float[]>();
        foreach (var (slot, file) in _settings.CustomSounds.ToArray())
        {
            if (!Enum.TryParse<SoundEffect>(slot, out var effect) || _library?.PathFor(file) is not { } path) continue;
            try
            {
                clips[effect] = CustomAudioLoader.LoadClip(path, CustomAudio.MaxSoundSeconds);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not load custom sound " + slot, ex);
            }
        }

        LoopingFileSource? music = null;
        if (_library?.PathFor(_settings.CustomMusic) is { } musicPath)
        {
            try
            {
                music = new LoopingFileSource(musicPath);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not load custom menu music", ex);
            }
        }

        LoopingFileSource? old;
        lock (_gate)
        {
            _custom.Clear();
            foreach (var (effect, clip) in clips) _custom[effect] = clip;
            old = _customMusic;
            _customMusic = music;
            if (_ambience is not null)
            {
                // The mixer finishes any read in progress before removing the input, so the old file is safe to close.
                _mixer.RemoveMixerInput(_ambience);
                _ambience = null;
            }
        }
        old?.Dispose();
    }

    public bool HasCustomMusic
    {
        get
        {
            lock (_gate) return _customMusic is not null;
        }
    }

    public void Play(SoundEffect effect)
    {
        if (!_settings.SoundEffects) return;
        lock (_gate)
        {
            if (!_custom.TryGetValue(effect, out var clip) && (_bank is null || !_bank.Effects.TryGetValue(effect, out clip))) return;
            var now = DateTime.UtcNow;
            if (_lastPlayed.TryGetValue(effect, out var last) && now - last < TimeSpan.FromMilliseconds(35)) return;
            _lastPlayed[effect] = now;
            if (!EnsureOutput()) return;
            _mixer.AddMixerInput(new ClipProvider(clip, (float)_settings.EffectsVolume, _format));
            _lastActivity = now;
        }
    }

    /// <summary>Plays a decoded clip once (previewing a chosen file), even when menu sounds are turned off.</summary>
    public void PlayPreview(float[] clip)
    {
        lock (_gate)
        {
            if (!EnsureOutput()) return;
            _mixer.AddMixerInput(new ClipProvider(clip, (float)Math.Max(0.35, Math.Max(_settings.EffectsVolume, _settings.AmbienceVolume)), _format));
            _lastActivity = DateTime.UtcNow;
        }
    }

    /// <summary>Fades the menu music in or out (it plays only while the menu is in front).</summary>
    public void SetAmbience(bool active)
    {
        lock (_gate)
        {
            _ambienceWanted = active;
            var hasMusic = _customMusic is not null || _bank is { Ambience.Length: > 0 };
            var shouldPlay = active && _settings.Ambience && hasMusic;
            if (shouldPlay)
            {
                if (!EnsureOutput()) return;
                if (_ambience is null)
                {
                    ISampleProvider source = _customMusic is not null ? _customMusic : new LoopingBuffer(_bank!.Ambience, _format);
                    _ambience = new AmbienceProvider(source);
                    _mixer.AddMixerInput(_ambience);
                }
                _ambience.TargetVolume = (float)_settings.AmbienceVolume;
                _lastActivity = DateTime.UtcNow;
            }
            else if (_ambience is not null)
            {
                _ambience.TargetVolume = 0;
            }
        }
    }

    public void ApplySettings() => SetAmbience(_ambienceWanted);

    private bool EnsureOutput()
    {
        if (_output is not null && _output.PlaybackState == PlaybackState.Playing) return true;
        try
        {
            _output?.Dispose();
            _output = new WaveOutEvent { DesiredLatency = 90, NumberOfBuffers = 3 };
            _output.Init(_mixer);
            _output.Play();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Audio output unavailable", ex);
            _output?.Dispose();
            _output = null;
            return false;
        }
    }

    private void CheckIdle()
    {
        lock (_gate)
        {
            if (_ambience is { TargetVolume: 0, CurrentVolume: < 0.0005f })
            {
                _mixer.RemoveMixerInput(_ambience);
                _ambience = null;
            }
            if (_output is null) return;
            bool hasInputs;
            try
            {
                hasInputs = _mixer.MixerInputs.ToArray().Length > 0;
            }
            catch (InvalidOperationException)
            {
                return; // the audio thread changed the list mid-read; check again on the next tick
            }
            if (_ambience is null && !hasInputs && DateTime.UtcNow - _lastActivity > TimeSpan.FromSeconds(4))
            {
                _output.Dispose();
                _output = null;
            }
        }
    }

    /// <summary>Re-opens the device after sleep/resume or a default-device change.</summary>
    public void ResetDevice()
    {
        lock (_gate)
        {
            _output?.Dispose();
            _output = null;
            if (_ambience is not null) EnsureOutput();
        }
    }

    public static float GetSystemVolume()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            return -1;
        }
    }

    public static void SetSystemVolume(float value)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0, 1);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not set system volume", ex);
        }
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
        lock (_gate)
        {
            _output?.Dispose();
            _output = null;
            _customMusic?.Dispose();
            _customMusic = null;
        }
    }

    private sealed class ClipProvider : ISampleProvider
    {
        private readonly float[] _data;
        private readonly float _volume;
        private int _position;

        public ClipProvider(float[] data, float volume, WaveFormat format)
        {
            _data = data;
            _volume = volume;
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var n = Math.Min(count, _data.Length - _position);
            for (var i = 0; i < n; i++) buffer[offset + i] = _data[_position + i] * _volume;
            _position += n;
            return n;
        }
    }

    private sealed class LoopingBuffer : ISampleProvider
    {
        private readonly float[] _data;
        private int _position;

        public LoopingBuffer(float[] data, WaveFormat format)
        {
            _data = data;
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                buffer[offset + i] = _data[_position];
                _position = (_position + 1) % _data.Length;
            }
            return count;
        }
    }

    private sealed class AmbienceProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;

        public AmbienceProvider(ISampleProvider source)
        {
            _source = source;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;
        public float TargetVolume { get; set; }
        public float CurrentVolume { get; private set; }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read < count) Array.Clear(buffer, offset + read, count - read);
            var step = 1f / (SoundSynth.Rate * 2 * 0.8f);
            for (var i = 0; i < count; i++)
            {
                if (CurrentVolume < TargetVolume) CurrentVolume = Math.Min(TargetVolume, CurrentVolume + step);
                else if (CurrentVolume > TargetVolume) CurrentVolume = Math.Max(TargetVolume, CurrentVolume - step);
                buffer[offset + i] *= CurrentVolume;
            }
            return count;
        }
    }
}
