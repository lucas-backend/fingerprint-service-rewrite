using FingerprintService.Errors;
using Microsoft.AspNetCore.Mvc;

namespace FingerprintService.Infrastructure;

/// <summary>
/// Helpers for reading raw binary request bodies with strict size validation.
/// </summary>
public static class RawBodyReader
{
    /// <summary>
    /// Reads the entire request body and requires it to be exactly
    /// <paramref name="expectedSize"/> bytes.
    /// </summary>
    public static async Task<byte[]> ReadExactlyAsync(
        HttpRequest request,
        int expectedSize,
        CancellationToken cancellationToken)
    {
        byte[] body = await ReadAllAsync(request, cancellationToken);

        if (body.Length != expectedSize)
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidFingerprintImage,
                $"Invalid fingerprint image. Expected: {expectedSize}, Actual: {body.Length}");
        }

        return body;
    }

    public static async Task<byte[]> ReadAllAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();

        await request.Body.CopyToAsync(memoryStream, cancellationToken);

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Rejects requests that do not declare application/octet-stream.
    /// Missing / empty Content-Type is also rejected — the type must be explicit.
    /// </summary>
    public static void RequireOctetStream(HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContentType)
            || !request.ContentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                ApiErrorCodes.UnsupportedMediaType,
                "Content-Type harus application/octet-stream.");
        }
    }
}
