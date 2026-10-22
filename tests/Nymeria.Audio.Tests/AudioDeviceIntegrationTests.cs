namespace Nymeria.Audio.Tests;

/// <summary>
/// Device-dependent checks that are not part of the headless test run.
/// </summary>
/// <remarks>
/// CI runners vary in audio hardware and SDL audio can enumerate or open a
/// device as a side effect, so these tests are skipped rather than producing an
/// environment-dependent pass. They are exercised manually through
/// <c>examples/HelloAudio</c> on macOS; see <c>docs/AUDIO.md</c>.
/// </remarks>
public sealed class AudioDeviceIntegrationTests
{
    [Fact(Skip = "Requires a live playback device and the process main thread. " +
                 "Verified manually with examples/HelloAudio on macOS CoreAudio; see docs/AUDIO.md.")]
    public void Create_PlaysAClipAndDisposesCleanly()
    {
        using AudioSystem audio = AudioSystem.Create(new AudioSystemOptions(maxVoices: 4));
        using AudioClip clip = audio.LoadWav(
            new MemoryStream(TestWav.CreateSilenceWav()));
        using AudioVoice voice = audio.Play(clip, loop: true);

        audio.Listener.Position = System.Numerics.Vector3.Zero;
        audio.Update(1.0f / 60.0f);
        audio.Dispose();
    }

    [Fact(Skip = "Requires a live playback device and the process main thread. " +
                 "Verified manually with examples/HelloAudio on macOS CoreAudio; see docs/AUDIO.md.")]
    public void TryCreate_ReportsFailureWithoutThrowing()
    {
        bool created = AudioSystem.TryCreate(out AudioSystem? audio, out string? error);

        if (created)
        {
            Assert.NotNull(audio);
            audio.Dispose();
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    private static class TestWav
    {
        public static byte[] CreateSilenceWav()
        {
            // One frame of 16-bit mono silence at 48 kHz.
            return
            [
                (byte)'R', (byte)'I', (byte)'F', (byte)'F',
                38, 0, 0, 0,
                (byte)'W', (byte)'A', (byte)'V', (byte)'E',
                (byte)'f', (byte)'m', (byte)'t', (byte)' ',
                16, 0, 0, 0,
                1, 0,
                1, 0,
                0x80, 0xBB, 0, 0,
                0x00, 0x77, 0x01, 0x00,
                2, 0,
                16, 0,
                (byte)'d', (byte)'a', (byte)'t', (byte)'a',
                2, 0, 0, 0,
                0, 0,
            ];
        }
    }
}
