using Couchtop.Core.Audio;
using Couchtop.Core.Settings;
using Xunit;

namespace Couchtop.Tests;

public sealed class CustomAudioTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "couchtop-audio-" + Guid.NewGuid().ToString("N"));

    public CustomAudioTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Source(string name, int bytes = 64)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void Import_copies_the_file_and_keeps_the_display_name()
    {
        var library = new CustomAudioLibrary(Path.Combine(_root, "library"));
        var imported = library.Import(Source("My Song.MP3"), CustomAudio.MusicSlot);

        Assert.Equal("My Song", imported.Name);
        Assert.StartsWith("Music-", imported.File);
        Assert.EndsWith(".mp3", imported.File);
        Assert.NotNull(library.PathFor(imported));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("video.mp4")]
    public void Import_rejects_unsupported_files(string name)
    {
        var library = new CustomAudioLibrary(Path.Combine(_root, "library"));
        Assert.Throws<NotSupportedException>(() => library.Import(Source(name), "Select"));
    }

    [Fact]
    public void Import_rejects_missing_files()
    {
        var library = new CustomAudioLibrary(Path.Combine(_root, "library"));
        Assert.Throws<FileNotFoundException>(() => library.Import(Path.Combine(_root, "gone.wav"), "Select"));
    }

    [Theory]
    [InlineData(@"..\settings.json.mp3")]
    [InlineData(@"C:\Windows\media\chimes.wav")]
    [InlineData("sub/dir.wav")]
    [InlineData("plain.exe")]
    public void Settings_cannot_point_outside_the_audio_folder(string file)
    {
        Assert.Null(CustomAudio.Clean(new CustomAudioFile(file, "x")));
        var library = new CustomAudioLibrary(Path.Combine(_root, "library"));
        Assert.Null(library.PathFor(new CustomAudioFile(file, "x")));
    }

    [Fact]
    public void Normalize_drops_unknown_slots_and_bad_files()
    {
        var settings = new UserSettings
        {
            CustomMusic = new CustomAudioFile(@"..\evil.mp3", "Evil"),
            CustomSounds = new Dictionary<string, CustomAudioFile>
            {
                ["Select"] = new("Select-abc.wav", ""),
                ["SportSwing"] = new("SportSwing-abc.wav", "Swing"),
                ["Back"] = new("Back-abc.txt", "Text"),
            },
        }.Normalize();

        Assert.Null(settings.CustomMusic);
        Assert.Single(settings.CustomSounds);
        Assert.Equal("Select-abc", settings.CustomSounds["Select"].Name);
    }

    [Fact]
    public void Remove_unused_keeps_files_still_chosen()
    {
        var library = new CustomAudioLibrary(Path.Combine(_root, "library"));
        var keep = library.Import(Source("keep.wav"), "Select");
        var stale = library.Import(Source("stale.wav"), "Back");

        Assert.Equal(1, library.RemoveUnused(new CustomAudioFile?[] { keep, null }));
        Assert.NotNull(library.PathFor(keep));
        Assert.Null(library.PathFor(stale));
    }

    [Fact]
    public void Every_slot_is_unique()
    {
        Assert.Equal(CustomSoundSlots.All.Count, CustomSoundSlots.All.Select(s => s.Id).Distinct().Count());
        Assert.False(CustomSoundSlots.IsKnown(CustomAudio.MusicSlot));
    }
}
