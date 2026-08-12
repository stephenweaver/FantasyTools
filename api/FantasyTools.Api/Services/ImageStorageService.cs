using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

namespace FantasyTools.Api.Services;

public interface IImageStorageService
{
    /// <summary>
    /// The folder uploads are written to, or null in R2 mode. Startup hands this straight to the static
    /// file middleware -- nothing reads images back through application code.
    /// </summary>
    string LocalRoot { get; }

    /// <summary>Stores the bytes under the category's folder and returns the URL a browser should use.</summary>
    Task<string> Save(string category, string contentType, byte[] bytes);
}

/// <summary>
/// Card artwork storage. Mirrors the document store's split exactly: IMAGE_SERVICE=R2 puts objects in the
/// images bucket, anything else writes to a local folder, and IMAGE_SERVICE_LOCAL=local keeps debug runs
/// on disk while the same .env stays valid in production.
/// </summary>
/// <remarks>
/// This deliberately does not go through <c>IFileService</c>. That is bound to a single bucket
/// (<c>R2_BUCKET</c>) and serializes <c>BaseDocument</c> to JSON; artwork is raw bytes in a *different*,
/// publicly-readable bucket, so it gets its own S3 client. The R2 credentials are shared with the document
/// store -- only the bucket differs.
/// </remarks>
public class ImageStorageService : IImageStorageService
{
    // Kept in sync with the accept attribute on the upload control in the card creator.
    public static readonly Dictionary<string, string> AllowedTypes = new()
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp"
    };

    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Every kind of image gets its own folder, and the folder name is the category itself -- it is not
    /// configurable. The bucket holds nothing but images, so a settable root prefix bought nothing and
    /// only gave an empty value somewhere to hide (a blank one produced keys starting with a slash).
    /// Adding a kind of image is one entry here.
    /// </summary>
    public static readonly HashSet<string> Categories = new(StringComparer.OrdinalIgnoreCase) { "cards" };

    private readonly ILogger<ImageStorageService> _logger;
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucket;
    private readonly string _baseUrl;

    public ImageStorageService(ILogger<ImageStorageService> logger)
    {
        _logger = logger;

        if (!string.Equals(EnvironmentHelper.GetVar("IMAGE_SERVICE"), "R2", StringComparison.OrdinalIgnoreCase))
        {
            // IMAGES_FOLDER is the disk root the category folders hang off, and is local-only -- in R2
            // the bucket is that root. Whitespace-check, not a null-check: an env file that declares
            // IMAGES_FOLDER= with no value hands back "", which a ?? would sail straight past.
            var folder = EnvironmentHelper.GetVar("IMAGES_FOLDER")?.Trim();

            LocalRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(folder) ? @"C:\FantasyTools\Images" : folder);

            _logger.LogInformation("Images are stored on local disk under {Folder}.", LocalRoot);
            return;
        }

        // Same failure style as the Common package: refuse to start rather than accept uploads that
        // silently land nowhere, or hand the browser a URL on a host that was never configured.
        _bucket = Require("IMAGES_BUCKET");
        _baseUrl = Require("IMAGES_BASE_URL").TrimEnd('/');

        var credentials = new BasicAWSCredentials(Require("R2_ACCESS_KEY"), Require("R2_SECRET_KEY"));

        _s3Client = new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = Require("R2_CONNECTION_STRING"),
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        });

        _logger.LogInformation("Images are stored in R2 bucket {Bucket}, served from {BaseUrl}.", _bucket, _baseUrl);
    }

    public string LocalRoot { get; }

    public async Task<string> Save(string category, string contentType, byte[] bytes)
    {
        if (!Categories.Contains(category))
        {
            throw new ArgumentException($"Unknown image category '{category}'.");
        }

        // Both segments are ours: the folder comes from the allowlist above and the name is a fresh
        // GUID. Nothing caller-supplied reaches the key, so it can neither escape the folder nor
        // overwrite an existing object, and the URL can be cached forever.
        var key = $"{category.ToLowerInvariant()}/{Guid.NewGuid():N}{AllowedTypes[contentType]}";

        if (LocalRoot != null)
        {
            var path = Path.Combine(LocalRoot, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);

            // Relative on purpose. The browser reaches the API through the Vite proxy in dev and the
            // nginx /api proxy in prod, so a relative URL works in both without knowing either host.
            return $"/api/images/{key}";
        }

        using var stream = new MemoryStream(bytes);

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            // R2 rejects the streaming-signature payloads the SDK sends by default.
            DisablePayloadSigning = true
        });

        return $"{_baseUrl}/{key}";
    }

    private static string Require(string name)
    {
        var value = EnvironmentHelper.GetVar(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"IMAGE_SERVICE is set to R2 but {name} is empty. Set the images variables, " +
                "or set IMAGE_SERVICE to something else to store artwork on local disk.");
        }

        return value;
    }
}
