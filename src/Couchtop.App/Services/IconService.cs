using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Couchtop.Core.Diagnostics;
using Couchtop.Core.Native;

namespace Couchtop.App.Services;

public sealed record IconResult(BitmapSource Image, Color Dominant);

/// <summary>Loads shell icons/thumbnails on background STA threads with a PNG disk cache for fast startup.</summary>
public sealed class IconService : IDisposable
{
    private readonly StaWorkQueue _queue = new(2, "Icon loader");
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, Task<IconResult?>> _memory = new(StringComparer.OrdinalIgnoreCase);

    public IconService(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(cacheDirectory);
    }

    public Task<IconResult?> GetAsync(string? parsingName, int size = 256, bool iconOnly = true, bool useDiskCache = true)
    {
        if (string.IsNullOrWhiteSpace(parsingName)) return Task.FromResult<IconResult?>(null);
        var key = $"{parsingName}|{size}|{iconOnly}";
        return _memory.GetOrAdd(key, _ => LoadAsync(parsingName, size, iconOnly, useDiskCache));
    }

    /// <summary>Loads without memory or disk caching (file browser, photos) so browsing many folders never grows memory.</summary>
    public Task<IconResult?> LoadUncachedAsync(string parsingName, int size, bool iconOnly) => LoadAsync(parsingName, size, iconOnly, useDiskCache: false);

    public void Forget(string parsingName)
    {
        foreach (var key in _memory.Keys.Where(k => k.StartsWith(parsingName + "|", StringComparison.OrdinalIgnoreCase)).ToList())
            _memory.TryRemove(key, out _);
    }

    private async Task<IconResult?> LoadAsync(string parsingName, int size, bool iconOnly, bool useDiskCache)
    {
        var cacheFile = useDiskCache ? CachePath(parsingName, size, iconOnly) : null;
        if (cacheFile is not null && File.Exists(cacheFile))
        {
            var cached = await Task.Run(() => LoadPng(cacheFile)).ConfigureAwait(false);
            if (cached is not null) return cached;
        }

        RawImage? raw;
        try
        {
            raw = await _queue.Enqueue(() => ShellImageLoader.Load(parsingName, size, iconOnly)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn($"Icon load failed for {parsingName}", ex);
            return null;
        }
        if (raw is null) return null;

        var bitmap = BitmapSource.Create(raw.Width, raw.Height, 96, 96, PixelFormats.Bgra32, null, raw.Pixels, raw.Width * 4);
        bitmap.Freeze();
        var (r, g, b) = ShellImageLoader.DominantColor(raw);
        var result = new IconResult(bitmap, Color.FromRgb(r, g, b));

        if (cacheFile is not null)
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var fs = File.Create(cacheFile);
                encoder.Save(fs);
            }
            catch (Exception ex)
            {
                Log.Warn("Icon cache write failed", ex);
            }
        }
        return result;
    }

    private string CachePath(string parsingName, int size, bool iconOnly)
    {
        long stamp = 0;
        try
        {
            if (File.Exists(parsingName)) stamp = File.GetLastWriteTimeUtc(parsingName).Ticks;
        }
        catch
        {
        }
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{parsingName}|{size}|{iconOnly}|{stamp}")))[..24];
        return Path.Combine(_cacheDirectory, hash + ".png");
    }

    private static IconResult? LoadPng(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            if (frame.Format != PixelFormats.Bgra32) frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            frame.Freeze();
            var (r, g, b) = ShellImageLoader.DominantColor(new RawImage(frame.PixelWidth, frame.PixelHeight, pixels));
            return new IconResult(frame, Color.FromRgb(r, g, b));
        }
        catch
        {
            try { File.Delete(file); } catch { }
            return null;
        }
    }

    /// <summary>Decodes an image file at a reduced size off the UI thread (keeps memory low for photos and banners).</summary>
    public static Task<BitmapSource?> LoadImageFileAsync(string path, int decodeWidth) => Task.Run<BitmapSource?>(() =>
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(path, UriKind.Absolute);
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Image load failed: {path}", ex);
            return null;
        }
    });

    public void Dispose() => _queue.Dispose();
}
