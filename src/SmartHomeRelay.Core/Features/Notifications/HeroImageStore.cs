namespace SmartHomeRelay.Core.Features.Notifications;

/// <summary>
/// Keeps downloaded notification images as files, because Windows toasts of an unpackaged app can only show
/// local images (spec 5.6). Files get random names in a folder of their own; the folder is swept of files older
/// than the toast expiration, at start and every hour, so images stay while their toast can still be opened.
/// </summary>
public sealed class HeroImageStore
{
    private static readonly Dictionary<string, string> s_extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/gif"] = ".gif",
    };

    private readonly TimeProvider _time;

    /// <summary>Creates the store in <paramref name="directory"/>.</summary>
    public HeroImageStore(string directory, TimeProvider time)
    {
        Directory = directory;
        _time = time;
    }

    /// <summary>The folder holding the images.</summary>
    public string Directory { get; }

    /// <summary>True for image types a toast can show (PNG, JPEG, GIF).</summary>
    public static bool IsSupported(string contentType) => s_extensions.ContainsKey(contentType);

    /// <summary>Saves the image and returns its path.</summary>
    public async Task<string> SaveAsync(byte[] image, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!s_extensions.TryGetValue(contentType, out var extension))
        {
            throw new ArgumentException($"Unsupported image type '{contentType}'.", nameof(contentType));
        }

        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, Guid.NewGuid().ToString("N") + extension);
        await File.WriteAllBytesAsync(path, image, cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <summary>Deletes images last written more than <paramref name="age"/> ago. Returns how many were deleted.</summary>
    public int Sweep(TimeSpan age)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return 0;
        }

        var cutoff = _time.GetUtcNow().UtcDateTime - age;
        var deleted = 0;
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use or gone; the next sweep tries again.
            }
        }

        return deleted;
    }
}
