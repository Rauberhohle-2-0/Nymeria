using System.Numerics;
using System.Text;
using Nymeria.Audio;
using Nymeria.Core.Audio;
using Nymeria.Graphics;

namespace HelloAudio;

/// <summary>
/// Owns the example's audio system, generated clips, and voices. Every resource
/// created here is disposed in reverse order.
/// </summary>
internal sealed class AudioDemo : IDisposable
{
    private const float AmbientGain = 0.25f;
    private const float SourceAMinDistance = 1.5f;
    private const float SourceAMaxDistance = 24.0f;
    private const float SourceBMinDistance = 2.0f;
    private const float SourceBMaxDistance = 32.0f;
    private const float SweepRadius = 4.0f;
    private const float SweepRadiansPerSecond = 0.6f;

    private readonly AudioSystem _audio;
    private readonly AudioClip _ambientClip;
    private readonly AudioClip _sourceAClip;
    private readonly AudioClip _sourceBClip;
    private readonly AudioVoice _ambient;
    private AudioVoice? _sourceA;
    private AudioVoice? _sourceB;
    private float _sourceAGain = 0.8f;
    private float _sourceBGain = 0.7f;
    private Vector3 _sourceAPosition = AudioDemoScene.SourceAPosition;
    private float _sweepAngle;
    private bool _sweepEnabled;
    private bool _disposed;

    private AudioDemo(
        AudioSystem audio,
        AudioClip ambientClip,
        AudioClip sourceAClip,
        AudioClip sourceBClip,
        AudioVoice ambient,
        AudioVoice sourceA,
        AudioVoice sourceB)
    {
        _audio = audio;
        _ambientClip = ambientClip;
        _sourceAClip = sourceAClip;
        _sourceBClip = sourceBClip;
        _ambient = ambient;
        _sourceA = sourceA;
        _sourceB = sourceB;
    }

    public bool IsDeviceLost => _audio.IsDeviceLost;

    /// <summary>
    /// Gets the current world-space position of spatial source A. It is fixed
    /// unless the left-to-right sweep is enabled.
    /// </summary>
    public Vector3 SourceAPosition => _sourceAPosition;

    public static AudioDemo? TryCreate(out string message)
    {
        if (!AudioSystem.TryCreate(
                new AudioSystemOptions(maxVoices: 8),
                out AudioSystem? audio,
                out string? error))
        {
            message = $"Audio unavailable: {error}";
            return null;
        }

        AudioSystem system = audio!;
        AudioClip? ambientClip = null;
        AudioClip? sourceAClip = null;
        AudioClip? sourceBClip = null;
        AudioVoice? ambient = null;
        AudioVoice? sourceA = null;
        AudioVoice? sourceB = null;

        try
        {
            using var ambientStream =
                new MemoryStream(GeneratedAudio.CreateAmbientWav());
            using var sourceAStream =
                new MemoryStream(GeneratedAudio.CreateSpatialToneWav(220.0));
            using var sourceBStream =
                new MemoryStream(GeneratedAudio.CreateSpatialToneWav(330.0));

            ambientClip = system.LoadWav(ambientStream);
            sourceAClip = system.LoadWav(sourceAStream);
            sourceBClip = system.LoadWav(sourceBStream);

            ambient = system.Play(ambientClip, loop: true);
            ambient.Gain = AmbientGain;

            sourceA = system.PlaySpatial(
                sourceAClip,
                AudioDemoScene.SourceAPosition,
                SourceAMinDistance,
                SourceAMaxDistance,
                loop: true);
            sourceA.Gain = 0.8f;

            sourceB = system.PlaySpatial(
                sourceBClip,
                AudioDemoScene.SourceBPosition,
                SourceBMinDistance,
                SourceBMaxDistance,
                loop: true);
            sourceB.Gain = 0.7f;

            var demo = new AudioDemo(
                system,
                ambientClip,
                sourceAClip,
                sourceBClip,
                ambient,
                sourceA,
                sourceB);

            message =
                $"Audio ready: {system.SampleRate} Hz stereo, float32 mixing, " +
                $"{system.MaxVoices} voice limit.";
            return demo;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ambient?.Dispose();
            sourceA?.Dispose();
            sourceB?.Dispose();
            system.Dispose();
            ambientClip?.Dispose();
            sourceAClip?.Dispose();
            sourceBClip?.Dispose();
            message = $"Audio unavailable: {exception.Message}";
            return null;
        }
    }

    /// <summary>
    /// Copies the camera into the listener and performs the per-frame audio
    /// update. No device or clip is recreated.
    /// </summary>
    public void Update(Camera camera, float deltaSeconds)
    {
        _audio.Listener.Position = camera.Position;
        _audio.Listener.Forward = camera.Forward;
        _audio.Listener.Up = Vector3.UnitY;
        UpdateSweep(deltaSeconds);
        _audio.Update(deltaSeconds);
    }

    /// <summary>
    /// Moves source A around the listener on a horizontal circle at a constant
    /// radius, so the left/right pan changes while distance and gains stay put.
    /// </summary>
    private void UpdateSweep(float deltaSeconds)
    {
        if (!_sweepEnabled)
        {
            return;
        }

        _sweepAngle = (_sweepAngle + (deltaSeconds * SweepRadiansPerSecond)) % MathF.Tau;

        Vector3 right = _audio.Listener.Right;

        if (right.LengthSquared() <= 1e-12f)
        {
            right = Vector3.UnitX;
        }

        Vector3 forward = _audio.Listener.Forward;

        _sourceAPosition =
            _audio.Listener.Position +
            (SweepRadius * MathF.Sin(_sweepAngle) * right) +
            (SweepRadius * MathF.Cos(_sweepAngle) * forward);

        if (_sourceA is not null)
        {
            _sourceA.Position = _sourceAPosition;
        }
    }

    public void ToggleAmbientPause()
    {
        if (_audio.IsDeviceLost)
        {
            return;
        }

        if (_ambient.State == AudioVoiceState.Playing)
        {
            _ambient.Pause();
        }
        else if (_ambient.State == AudioVoiceState.Paused)
        {
            _ambient.Resume();
        }
    }

    public void ToggleSourceAPause()
    {
        if (_sourceA is null ||
            _sourceA.State is not (AudioVoiceState.Playing or AudioVoiceState.Paused))
        {
            return;
        }

        if (_sourceA.State == AudioVoiceState.Playing)
        {
            _sourceA.Pause();
        }
        else
        {
            _sourceA.Resume();
        }
    }

    /// <summary>
    /// Stops source A and releases its voice, or starts a new voice for the same
    /// shared clip. This demonstrates explicit stop/restart ownership.
    /// </summary>
    public void ToggleSourceA()
    {
        if (_audio.IsDeviceLost)
        {
            return;
        }

        if (_sourceA is not null &&
            _sourceA.State is AudioVoiceState.Playing or AudioVoiceState.Paused)
        {
            _sourceA.Dispose();
            _sourceA = null;
            return;
        }

        _sourceA?.Dispose();
        _sourceA = _audio.PlaySpatial(
            _sourceAClip,
            _sourceAPosition,
            SourceAMinDistance,
            SourceAMaxDistance,
            loop: true);
        _sourceA.Gain = _sourceAGain;
    }

    /// <summary>
    /// Adjusts the global positional stereo effect strength by <paramref name="delta"/>.
    /// It is applied to the next mix block without restarting any voice.
    /// </summary>
    public void AdjustStereoEffectStrength(float delta)
    {
        _audio.StereoEffectStrength = Math.Clamp(
            _audio.StereoEffectStrength + delta,
            0.0f,
            1.0f);
    }

    /// <summary>
    /// Cycles the positional stereo effect strength through 0%, 50%, and 100%,
    /// which makes the three documented settings easy to compare by ear.
    /// </summary>
    public void CycleStereoEffectPreset()
    {
        float current = _audio.StereoEffectStrength;

        _audio.StereoEffectStrength = current switch
        {
            < 0.25f => 0.5f,
            < 0.75f => 1.0f,
            _ => 0.0f,
        };
    }

    /// <summary>
    /// Starts or stops moving source A left to right around the listener.
    /// </summary>
    public void ToggleSweep()
    {
        _sweepEnabled = !_sweepEnabled;

        if (!_sweepEnabled)
        {
            _sweepAngle = 0.0f;
            _sourceAPosition = AudioDemoScene.SourceAPosition;
        }

        if (_sourceA is not null &&
            _sourceA.State is AudioVoiceState.Playing or AudioVoiceState.Paused)
        {
            _sourceA.Position = _sourceAPosition;
        }
    }

    public void AdjustMasterGain(float delta)
    {
        _audio.MasterGain = Math.Clamp(_audio.MasterGain + delta, 0.0f, 1.0f);
    }

    public void AdjustSourceAGain(float delta)
    {
        _sourceAGain = Math.Clamp(_sourceAGain + delta, 0.0f, 1.0f);

        if (_sourceA is not null &&
            _sourceA.State is AudioVoiceState.Playing or AudioVoiceState.Paused)
        {
            _sourceA.Gain = _sourceAGain;
        }
    }

    public string BuildStatus()
    {
        var builder = new StringBuilder();

        builder.Append(
            $"audio: {_audio.SampleRate} Hz stereo, " +
            $"{_audio.ActiveVoiceCount}/{_audio.MaxVoices} voices, " +
            $"master {Percent(_audio.MasterGain)}, " +
            $"positional stereo {Percent(_audio.StereoEffectStrength)} " +
            $"(0% centers, 100% full pan), " +
            $"device {(_audio.IsDeviceLost ? "lost" : "ok")}");
        builder.AppendLine();
        builder.AppendLine(
            $"ambient (2D loop): {DescribeVoice(_ambient, AmbientGain)}");
        builder.AppendLine(
            $"source A (3D loop{(_sweepEnabled ? ", sweeping" : "")}): {DescribeSpatialVoice(
                _sourceA,
                _sourceAPosition,
                SourceAMinDistance,
                SourceAMaxDistance,
                _sourceAGain)}");
        builder.Append(
            $"source B (3D loop): {DescribeSpatialVoice(
                _sourceB,
                AudioDemoScene.SourceBPosition,
                SourceBMinDistance,
                SourceBMaxDistance,
                _sourceBGain)}");

        return builder.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Voices release their clip references first. Disposing the system then
        // disposes any voice still alive and shuts down the device. Clips are
        // disposed last because voices reference their samples.
        _ambient.Dispose();
        _sourceA?.Dispose();
        _sourceB?.Dispose();
        _audio.Dispose();

        _ambientClip.Dispose();
        _sourceAClip.Dispose();
        _sourceBClip.Dispose();
    }

    private string DescribeSpatialVoice(
        AudioVoice? voice,
        Vector3 position,
        float minDistance,
        float maxDistance,
        float gain)
    {
        if (voice is null)
        {
            return "stopped (no voice)";
        }

        Vector3 listenerPosition = _audio.Listener.Position;
        float distance = Vector3.Distance(listenerPosition, position);
        float attenuation = AudioMath.DistanceAttenuation(
            listenerPosition,
            position,
            minDistance,
            maxDistance);
        float pan = AudioMath.StereoPan(
            listenerPosition,
            _audio.Listener.Right,
            position);
        (float left, float right) = AudioMath.SpatialGains(
            pan,
            _audio.StereoEffectStrength);

        return
            $"{DescribeState(voice.State)}, " +
            $"pos ({position.X:0.0}, {position.Y:0.0}, {position.Z:0.0}), " +
            $"distance {distance:0.0}, attenuation {attenuation:0.00}, " +
            $"pan {FormatPan(pan)} (L {left:0.00} / R {right:0.00}), " +
            $"gain {Percent(gain)}";
    }

    private static string DescribeVoice(AudioVoice voice, float gain) =>
        $"{DescribeState(voice.State)}, gain {Percent(gain)}";

    private static string DescribeState(AudioVoiceState state) => state switch
    {
        AudioVoiceState.Playing => "playing",
        AudioVoiceState.Paused => "paused",
        AudioVoiceState.Stopped => "stopped",
        AudioVoiceState.Ended => "ended",
        AudioVoiceState.Disposed => "disposed",
        _ => state.ToString(),
    };

    private static string FormatPan(float pan)
    {
        if (pan < -0.02f)
        {
            return $"left {-pan:0.00}";
        }

        if (pan > 0.02f)
        {
            return $"right {pan:0.00}";
        }

        return "center";
    }

    private static string Percent(float value) =>
        $"{MathF.Round(value * 100.0f):0}%";
}
