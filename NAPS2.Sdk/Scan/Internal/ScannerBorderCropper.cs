using NAPS2.Images.Bitwise;

namespace NAPS2.Scan.Internal;

/// <summary>
/// Detects narrow dark/grey scanner strips at the outer edge of ADF scans.
/// Unlike SafeAutoCropper's conservative near-black detector, this detector compares
/// the edge against the actual page interior so grey gradients and partially lit
/// scanner borders can be removed without treating a genuinely dark page as a border.
/// </summary>
internal static class ScannerBorderCropper
{
    private const double MAX_BORDER_SEARCH_FRACTION = 0.05;
    private const double MAX_BORDER_INCHES = 0.24; // about 6 mm at any DPI
    private const int MAX_SAMPLES_PER_DIMENSION = 1200;
    private const int MIN_LUMA_DELTA = 34;
    private const int ABSOLUTE_DARK_LUMA = 88;
    private const double MIN_DARK_SAMPLE_FRACTION = 0.32;
    private const double MIN_ABSOLUTE_DARK_FRACTION = 0.38;
    private const int MAX_GAP_LINES = 3;
    private const int MIN_INTERIOR_LUMA = 108;

    public static ScannerBorderCropResult? Detect(IMemoryImage image)
    {
        if (image.Width < 100 || image.Height < 100)
        {
            return null;
        }

        using var reader = new RgbPixelReader(image);
        int interiorLuma = EstimateInteriorLuma(reader, image.Width, image.Height);

        // A dark sheet (green/brown/black cover, old dossier paper, etc.) must not be
        // interpreted as a scanner strip merely because its outer edge is dark.
        if (interiorLuma < MIN_INTERIOR_LUMA)
        {
            return null;
        }

        int maxX = GetBorderSearchPixels(image.Width, image.HorizontalResolution);
        int maxY = GetBorderSearchPixels(image.Height, image.VerticalResolution);
        int stepX = Math.Max(1, image.Width / MAX_SAMPLES_PER_DIMENSION);
        int stepY = Math.Max(1, image.Height / MAX_SAMPLES_PER_DIMENSION);

        int left = FindVerticalBorder(reader, image.Width, image.Height, 0, maxX, 1, stepY, interiorLuma);
        int rightIndex = FindVerticalBorder(reader, image.Width, image.Height,
            image.Width - 1, image.Width - 1 - maxX, -1, stepY, interiorLuma);
        int right = rightIndex < image.Width ? image.Width - 1 - rightIndex : 0;
        int top = FindHorizontalBorder(reader, image.Width, image.Height, 0, maxY, 1, stepX, interiorLuma);
        int bottomIndex = FindHorizontalBorder(reader, image.Width, image.Height,
            image.Height - 1, image.Height - 1 - maxY, -1, stepX, interiorLuma);
        int bottom = bottomIndex < image.Height ? image.Height - 1 - bottomIndex : 0;

        if (left == 0 && right == 0 && top == 0 && bottom == 0)
        {
            return null;
        }

        // A narrow scanner-edge cleanup should never materially change the page area.
        int resultWidth = image.Width - left - right;
        int resultHeight = image.Height - top - bottom;
        if (resultWidth < image.Width * 0.90 || resultHeight < image.Height * 0.90)
        {
            return null;
        }

        return new ScannerBorderCropResult(
            new CropTransform(left, right, top, bottom, image.Width, image.Height),
            interiorLuma);
    }

    private static int FindVerticalBorder(RgbPixelReader reader, int width, int height,
        int start, int end, int direction, int sampleStep, int interiorLuma)
    {
        int lastBorder = -1;
        int gaps = 0;
        int yStart = Math.Max(2, height / 100);
        int yEnd = Math.Min(height - 2, height - yStart);

        for (int x = start; direction > 0 ? x <= end : x >= end; x += direction)
        {
            if (LineLooksLikeScannerBorder(
                    sample: y => reader[y, x], start: yStart, end: yEnd,
                    step: sampleStep, interiorLuma: interiorLuma))
            {
                lastBorder = x;
                gaps = 0;
            }
            else if (lastBorder >= 0 && ++gaps > MAX_GAP_LINES)
            {
                break;
            }
        }

        if (lastBorder < 0)
        {
            return direction > 0 ? 0 : width;
        }
        return direction > 0 ? Math.Min(width - 1, lastBorder + 1) : Math.Max(0, lastBorder - 1);
    }

    private static int FindHorizontalBorder(RgbPixelReader reader, int width, int height,
        int start, int end, int direction, int sampleStep, int interiorLuma)
    {
        int lastBorder = -1;
        int gaps = 0;
        int xStart = Math.Max(2, width / 100);
        int xEnd = Math.Min(width - 2, width - xStart);

        for (int y = start; direction > 0 ? y <= end : y >= end; y += direction)
        {
            if (LineLooksLikeScannerBorder(
                    sample: x => reader[y, x], start: xStart, end: xEnd,
                    step: sampleStep, interiorLuma: interiorLuma))
            {
                lastBorder = y;
                gaps = 0;
            }
            else if (lastBorder >= 0 && ++gaps > MAX_GAP_LINES)
            {
                break;
            }
        }

        if (lastBorder < 0)
        {
            return direction > 0 ? 0 : height;
        }
        return direction > 0 ? Math.Min(height - 1, lastBorder + 1) : Math.Max(0, lastBorder - 1);
    }

    private static bool LineLooksLikeScannerBorder(Func<int, (int r, int g, int b)> sample,
        int start, int end, int step, int interiorLuma)
    {
        int samples = 0;
        int relativeDark = 0;
        int absoluteDark = 0;

        for (int i = start; i < end; i += step)
        {
            var color = sample(i);
            int luma = Luma(color);
            if (luma <= interiorLuma - MIN_LUMA_DELTA)
            {
                relativeDark++;
            }
            if (luma <= ABSOLUTE_DARK_LUMA && IsApproximatelyNeutral(color))
            {
                absoluteDark++;
            }
            samples++;
        }

        if (samples == 0)
        {
            return false;
        }

        return relativeDark / (double) samples >= MIN_DARK_SAMPLE_FRACTION ||
               absoluteDark / (double) samples >= MIN_ABSOLUTE_DARK_FRACTION;
    }

    private static int EstimateInteriorLuma(RgbPixelReader reader, int width, int height)
    {
        var values = new List<int>();
        int x0 = width / 8;
        int x1 = width - x0;
        int y0 = height / 8;
        int y1 = height - y0;
        int stepX = Math.Max(1, (x1 - x0) / 80);
        int stepY = Math.Max(1, (y1 - y0) / 100);

        for (int y = y0; y < y1; y += stepY)
        {
            for (int x = x0; x < x1; x += stepX)
            {
                values.Add(Luma(reader[y, x]));
            }
        }

        if (values.Count == 0)
        {
            return 255;
        }

        // Use an upper percentile rather than the mean. Text, stamps and signatures
        // should not make a white/aged-paper page look artificially dark.
        values.Sort();
        int index = (int) Math.Round((values.Count - 1) * 0.80);
        return values[index];
    }

    private static int GetBorderSearchPixels(int dimension, float resolution)
    {
        int byFraction = Math.Max(2, (int) Math.Round(dimension * MAX_BORDER_SEARCH_FRACTION));
        if (resolution <= 0)
        {
            return byFraction;
        }
        int byPhysicalSize = Math.Max(2, (int) Math.Round(resolution * MAX_BORDER_INCHES));
        return Math.Min(byFraction, byPhysicalSize);
    }

    private static int Luma((int r, int g, int b) color) =>
        (color.r * 299 + color.g * 587 + color.b * 114) / 1000;

    private static bool IsApproximatelyNeutral((int r, int g, int b) color)
    {
        int max = Math.Max(color.r, Math.Max(color.g, color.b));
        int min = Math.Min(color.r, Math.Min(color.g, color.b));
        return max - min <= 34;
    }
}

internal record ScannerBorderCropResult(CropTransform Transform, int InteriorLuma);
