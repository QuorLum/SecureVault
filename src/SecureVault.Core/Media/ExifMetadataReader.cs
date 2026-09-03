using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace SecureVault.Core.Media;

public record ExifData(
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    string? DateTaken,
    string? ExposureTime,
    string? FNumber,
    string? IsoSpeed,
    string? FocalLength,
    int? Width,
    int? Height,
    string? GpsCoordinates);

/// <summary>
/// Extracts EXIF and photo metadata directly from memory streams (H05).
/// </summary>
public static class ExifMetadataReader
{
    public static ExifData Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(stream);

            string? make = null;
            string? model = null;
            string? lens = null;
            string? date = null;
            string? exposure = null;
            string? fNumber = null;
            string? iso = null;
            string? focal = null;
            int? width = null;
            int? height = null;
            string? gps = null;

            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd != null)
            {
                date = subIfd.GetDescription(ExifDirectoryBase.TagDateTimeOriginal)
                    ?? subIfd.GetDescription(ExifDirectoryBase.TagDateTimeDigitized);
                exposure = subIfd.GetDescription(ExifDirectoryBase.TagExposureTime);
                fNumber = subIfd.GetDescription(ExifDirectoryBase.TagFNumber);
                iso = subIfd.GetDescription(ExifDirectoryBase.TagIsoEquivalent);
                focal = subIfd.GetDescription(ExifDirectoryBase.TagFocalLength);
                lens = subIfd.GetDescription(ExifDirectoryBase.TagLensModel);
                width = subIfd.GetInt32(ExifDirectoryBase.TagExifImageWidth);
                height = subIfd.GetInt32(ExifDirectoryBase.TagExifImageHeight);
            }

            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 != null)
            {
                make ??= ifd0.GetDescription(ExifDirectoryBase.TagMake);
                model ??= ifd0.GetDescription(ExifDirectoryBase.TagModel);
                date ??= ifd0.GetDescription(ExifDirectoryBase.TagDateTime);
                width ??= ifd0.GetInt32(ExifDirectoryBase.TagImageWidth);
                height ??= ifd0.GetInt32(ExifDirectoryBase.TagImageHeight);
            }

            var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();
            if (gpsDir != null)
            {
                var location = gpsDir.GetGeoLocation();
                if (location.HasValue && !location.Value.IsZero)
                {
                    gps = $"{location.Value.Latitude:0.00000}°, {location.Value.Longitude:0.00000}°";
                }
            }

            return new ExifData(make, model, lens, date, exposure, fNumber, iso, focal, width, height, gps);
        }
        catch
        {
            return new ExifData(null, null, null, null, null, null, null, null, null, null, null);
        }
    }
}
