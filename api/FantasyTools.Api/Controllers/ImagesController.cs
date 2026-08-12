using FantasyTools.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FantasyTools.Api.Controllers;

/// <summary>
/// Image uploads. Requires a bearer token: it is the one route that accepts arbitrary bytes.
/// </summary>
/// <remarks>
/// There is no read action to match. In R2 mode the images host serves the objects directly, and in
/// local mode Startup points the static file middleware at the same folder -- so reads never enter
/// application code, and no caller-supplied string is ever turned into a file path.
/// </remarks>
[ApiController]
[Route("api/images")]
public class ImagesController(IImageStorageService images) : ControllerBase
{
    /// <summary>
    /// Uploads one image of the named kind. The category is the folder it lands in -- <c>cards</c> is
    /// the only one today, and a new kind of image is a new value in
    /// <see cref="ImageStorageService.Categories"/> rather than a change here.
    /// </summary>
    [HttpPost("{category}")]
    [Authorize]
    [RequestSizeLimit(ImageStorageService.MaxBytes + 1024)]
    public async Task<ActionResult> Upload(string category, IFormFile file)
    {
        if (!ImageStorageService.Categories.Contains(category))
        {
            return NotFound($"Unknown image category '{category}'.");
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("Choose an image to upload.");
        }

        if (file.Length > ImageStorageService.MaxBytes)
        {
            return BadRequest($"Images must be smaller than {ImageStorageService.MaxBytes / (1024 * 1024)} MB.");
        }

        var contentType = file.ContentType?.Split(';')[0].Trim().ToLowerInvariant();

        if (contentType == null || !ImageStorageService.AllowedTypes.ContainsKey(contentType))
        {
            return BadRequest("Artwork must be a PNG, JPG, or WebP image.");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        // The declared content type is caller-supplied and these objects are served publicly, so the
        // bytes have to agree with it -- otherwise the bucket becomes a host for anything at all.
        if (!LooksLike(contentType, bytes))
        {
            return BadRequest("That file is not a valid PNG, JPG, or WebP image.");
        }

        return Ok(new { url = await images.Save(category, contentType, bytes) });
    }

    private static bool LooksLike(string contentType, byte[] bytes) => contentType switch
    {
        "image/png" => Starts(bytes, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "image/jpeg" => Starts(bytes, [0xFF, 0xD8, 0xFF]),
        "image/webp" => Starts(bytes, "RIFF"u8) && bytes.Length > 12 && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };

    private static bool Starts(byte[] bytes, ReadOnlySpan<byte> signature) =>
        bytes.Length >= signature.Length && bytes.AsSpan(0, signature.Length).SequenceEqual(signature);
}
