namespace FingerprintService.Imaging;

/// <summary>
/// R503 image protocol invariants. These are protocol constants, not
/// configurable values: changing them changes the wire format and database
/// template compatibility.
/// </summary>
public static class R503ImageFormat
{
    public const int Width = 192;

    public const int Height = 192;

    /// <summary>192 x 192 = 36,864 pixels.</summary>
    public const int PixelCount = Width * Height;

    /// <summary>
    /// Two 4-bit pixels are packed per byte: 36,864 / 2 = 18,432 bytes.
    /// </summary>
    public const int PackedSize = PixelCount / 2;

    /// <summary>One byte per pixel = 36,864 bytes.</summary>
    public const int GrayscaleSize = PixelCount;
}
