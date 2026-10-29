using System.Globalization;
using System.Text;

namespace FingerprintService.Imaging;

public readonly record struct ImageStatistics(
    int PixelCount,
    byte Min,
    byte Max,
    double Average);

/// <summary>
/// R503 image decoding utilities. Pure, stateless and thread-safe.
///
/// IMPORTANT: the nibble unpacking and 4-bit to 8-bit conversion below is the
/// wire-format contract with the R503 sensor and must not be changed without
/// regression evidence.
/// </summary>
public sealed class R503ImageService
{
    /// <summary>
    /// Converts packed 4-bit R503 data into 8-bit grayscale.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the input is not exactly <see cref="R503ImageFormat.PackedSize"/> bytes.
    /// </exception>
    public byte[] UnpackImage(byte[] r503Data)
    {
        ArgumentNullException.ThrowIfNull(r503Data);

        ValidatePackedSize(r503Data.Length);

        byte[] image = new byte[R503ImageFormat.GrayscaleSize];

        int outputIndex = 0;

        foreach (byte value in r503Data)
        {
            // Pixel pertama = high nibble
            int highNibble = (value >> 4) & 0x0F;

            // Pixel kedua = low nibble
            int lowNibble = value & 0x0F;

            // Konversi 4-bit -> 8-bit (nibble * 17, where 17 = 255 / 15)
            image[outputIndex++] = (byte)(highNibble * 17);
            image[outputIndex++] = (byte)(lowNibble * 17);
        }

        return image;
    }

    public bool IsValidPackedSize(int length) => length == R503ImageFormat.PackedSize;

    public void ValidatePackedSize(int length)
    {
        if (!IsValidPackedSize(length))
        {
            throw new ArgumentException(
                $"Ukuran data R503 tidak sesuai. " +
                $"Expected: {R503ImageFormat.PackedSize}, Actual: {length}",
                nameof(length));
        }
    }

    public byte[] CreatePgm(byte[] grayscaleImage)
    {
        ArgumentNullException.ThrowIfNull(grayscaleImage);

        if (grayscaleImage.Length != R503ImageFormat.GrayscaleSize)
        {
            throw new ArgumentException(
                "Ukuran grayscale image tidak sesuai.",
                nameof(grayscaleImage));
        }

        string header =
            $"P5\n{R503ImageFormat.Width} {R503ImageFormat.Height}\n255\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        byte[] pgm = new byte[headerBytes.Length + grayscaleImage.Length];

        Buffer.BlockCopy(headerBytes, 0, pgm, 0, headerBytes.Length);
        Buffer.BlockCopy(
            grayscaleImage,
            0,
            pgm,
            headerBytes.Length,
            grayscaleImage.Length);

        return pgm;
    }

    public ImageStatistics GetStatistics(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Length == 0)
        {
            return new ImageStatistics(0, 0, 0, 0);
        }

        byte min = 255;
        byte max = 0;
        long total = 0;

        foreach (byte pixel in image)
        {
            if (pixel < min)
            {
                min = pixel;
            }

            if (pixel > max)
            {
                max = pixel;
            }

            total += pixel;
        }

        double average = (double)total / image.Length;

        return new ImageStatistics(image.Length, min, max, average);
    }

    /// <summary>
    /// Formats statistics for diagnostic logging. Values only; never biometric data.
    /// </summary>
    public string FormatStatistics(ImageStatistics statistics)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Width: {R503ImageFormat.Width}, Height: {R503ImageFormat.Height}, " +
            $"PixelCount: {statistics.PixelCount}, Min: {statistics.Min}, " +
            $"Max: {statistics.Max}, Average: {statistics.Average:F2}");
    }
}
