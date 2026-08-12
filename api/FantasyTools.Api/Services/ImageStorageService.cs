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

    /// <summary>Stores the bytes and returns the URL a browser should use to fetch them.</summary>
    Task<string> Save(string contentType, byte[] bytes);
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

    private readonly ILogger<ImageStorageService> _logger;
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucket;
    private readonly string _baseUrl;

    /// <summary>The leading segment of every key. Part of the stored URL, so it must stay relative.</summary>
    private readonly string _prefix;

    public ImageStorageService(ILogger<ImageStorageService> logger)
    {
        _logger = logger;

        if (!string.Equals(EnvironmentHelper.GetVar("IMAGE_SERVICE"), "R2", StringComparison.OrdinalIgnoreCase))
        {
            // IMAGES_FOLDER means the same thing DOCUMENTS_FOLDER does: a disk path locally, an object
            // key prefix in R2. Locally it is the root that keys hang off, never part of a key itself.
            LocalRoot = Path.GetFullPath(EnvironmentHelper.GetVar("IMAGES_FOLDER") ?? @"C:\FantasyTools\Images");
            _prefix = "cards";
            _logger.LogInformation("Card artwork is stored on local disk at {Folder}.", LocalRoot);
            return;
        }

        // Same failure style as the Common package: refuse to start rather than accept uploads that
        // silently land nowhere, or hand the browser a URL on a host that was never configured.
        _bucket = Require("IMAGES_BUCKET");
        _baseUrl = Require("IMAGES_BASE_URL").TrimEnd('/');
        _prefix = (EnvironmentHelper.GetVar("IMAGES_FOLDER") ?? "cards").Trim('/');

        var credentials = new BasicAWSCredentials(Require("R2_ACCESS_KEY"), Require("R2_SECRET_KEY"));

        _s3Client = new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = Require("R2_CONNECTION_STRING"),
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        });

        _logger.LogInformation("Card artwork is stored in R2 bucket {Bucket}, served from {BaseUrl}.", _bucket, _baseUrl);
    }

    public string LocalRoot { get; }

    public async Task<string> Save(string contentType, byte[] bytes)
    {
        // The name is a fresh GUID, so a key can never point anywhere but at the file just written and
        // the URL can be cached forever. Nothing caller-supplied reaches the path.
        var key = $"{_prefix}/{Guid.NewGuid():N}{AllowedTypes[contentType]}";

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
