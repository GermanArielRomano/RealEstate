using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace RealEstate.Infrastructure.Imaging;

public sealed class ImageOptions
{
    public const string Section = "Images";

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // por archivo
    public int MaxFilesPerRequest { get; set; } = 10;
    public int MaxImagesPerProperty { get; set; } = 30;

    public int MaxDimension { get; set; } = 1600;      // lado mayor de la imagen final
    public int ThumbnailDimension { get; set; } = 480; // lado mayor de la miniatura
    public int Quality { get; set; } = 80;             // WebP 0-100
    public int ThumbnailQuality { get; set; } = 75;

    public long MaxPixels { get; set; } = 50_000_000;  // anti "decompression bomb"
}

public sealed record OptimizedImage(byte[] Main, byte[] Thumbnail, int Width, int Height);

public sealed class InvalidImageException(string message) : Exception(message);

public interface IImageOptimizer
{
    /// <exception cref="InvalidImageException">El archivo no es una imagen soportada o es peligrosa.</exception>
    Task<OptimizedImage> OptimizeAsync(Stream input, CancellationToken ct = default);
}

public sealed class ImageSharpOptimizer(IOptions<ImageOptions> options) : IImageOptimizer
{
    private readonly ImageOptions _o = options.Value;

    public async Task<OptimizedImage> OptimizeAsync(Stream input, CancellationToken ct = default)
    {
        // El tamaño del archivo ya fue limitado en el controlador; se copia a memoria para leerlo dos veces.
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);

        // 1) Validar por CONTENIDO (no por extensión ni Content-Type, que el cliente puede falsear)
        //    y rechazar resoluciones absurdas ANTES de decodificar los píxeles.
        ImageInfo info;
        try
        {
            buffer.Position = 0;
            info = await Image.IdentifyAsync(buffer, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidImageException("El archivo no es una imagen válida.");
        }

        if (info.Metadata.DecodedImageFormat is not (JpegFormat or PngFormat or WebpFormat))
            throw new InvalidImageException("Formato no soportado. Usa JPEG, PNG o WebP.");

        if ((long)info.Width * info.Height > _o.MaxPixels)
            throw new InvalidImageException("La resolución de la imagen es demasiado alta.");

        // 2) Decodificar, normalizar y recomprimir
        try
        {
            buffer.Position = 0;
            using var image = await Image.LoadAsync(buffer, ct);

            // Solo el primer fotograma (descarta animaciones)
            while (image.Frames.Count > 1)
                image.Frames.RemoveFrame(1);

            // Aplica la rotación EXIF (fotos verticales del celular) y la deja "horneada" en los píxeles
            image.Mutate(x => x.AutoOrient());

            // Privacidad: elimina EXIF (incluye GPS de la foto), XMP e IPTC.
            // Se conserva el perfil ICC para no alterar los colores (p. ej. Display P3 de iPhone).
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;

            ResizeDown(image, _o.MaxDimension);
            var main = await EncodeAsync(image, _o.Quality, ct);
            var (width, height) = (image.Width, image.Height);

            using var thumb = image.Clone(_ => { });
            ResizeDown(thumb, _o.ThumbnailDimension);
            var thumbnail = await EncodeAsync(thumb, _o.ThumbnailQuality, ct);

            return new OptimizedImage(main, thumbnail, width, height);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidImageException)
        {
            throw new InvalidImageException("No se pudo procesar la imagen (archivo dañado).");
        }
    }

    /// <summary>Reduce manteniendo proporción; nunca agranda imágenes pequeñas.</summary>
    private static void ResizeDown(Image image, int maxSide)
    {
        if (image.Width <= maxSide && image.Height <= maxSide) return;

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(maxSide, maxSide),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));
    }

    private static async Task<byte[]> EncodeAsync(Image image, int quality, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await image.SaveAsync(ms, new WebpEncoder { Quality = quality, FileFormat = WebpFileFormatType.Lossy }, ct);
        return ms.ToArray();
    }
}
