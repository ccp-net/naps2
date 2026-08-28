using NAPS2.Images.Bitwise;

namespace NAPS2.Scan.Internal;

/// <summary>
/// Conservative post-scan paper/border detection used when a WIA/TWAIN driver accepts automatic sizing but still
/// returns the configured fallback canvas. The detector deliberately prefers keeping extra background over removing
/// dossier content. It only performs content-based cropping for a clearly smaller object, while neutral black scanner
/// borders can be removed independently.
/// </summary>
internal static class SafeAutoCropper
{
    private const double PAGE_SIZE_TOLERANCE_INCHES = 0.12;
    private const double MAX_BORDER_SEARCH_FRACTION = 0.025;
    private const double MAX_CONTENT_RESULT_AREA_FRACTION = 0.70;
    private const double MAX_CONTENT_RESULT_DIMENSION_FRACTION = 0.80;
    private const double MAX_UNCHANGED_DIMENSION_FRACTION = 0.98;
    private const double MIN_CONTENT_DIMENSION_FRACTION = 0.15;
    // A very low line threshold treated faint flatbed gradients and isolated dust near the far edge as paper. Require
    // a substantial run of foreground pixels so a smaller sheet touching the top/left scan origin can still be found
    // while low-density scanner noise in the remaining white canvas is ignored.
    private const double CONTENT_LINE_COVERAGE_FRACTION = 0.04;
    private const int CONTENT_COLOR_DISTANCE = 24;
    private const int CONTENT_LUMA_DISTANCE = 18;
    private const int MAX_SAMPLES_PER_DIMENSION = 1400;
    private const double SAFETY_MARGIN_INCHES = 0.10; // 2.54 mm

    public static SafeAutoCropResult? Detect(IMemoryImage image, PageSize? fallbackPageSize)
    {
        if (image.Width < 100 || image.Height < 100)
        {
            return null;
        }

        using var reader = new RgbPixelReader(image);
        var border = DetectNeutralBlackBorders(reader, image.Width, image.Height,
            image.HorizontalResolution, image.VerticalResolution);

        int left = border.Left;
        int right = border.Right;
        int top = border.Top;
        int bottom = border.Bottom;
        string reason = border.HasCrop ? "neutral-black-border" : "";

        if (MatchesFallbackCanvas(image, fallbackPageSize))
        {
            var content = DetectClearlySmallerContent(reader, image.Width, image.Height, border,
                image.HorizontalResolution, image.VerticalResolution);
            if (content.HasCrop)
            {
                left = Math.Max(left, content.Left);
                right = Math.Max(right, content.Right);
                top = Math.Max(top, content.Top);
                bottom = Math.Max(bottom, content.Bottom);
                reason = border.HasCrop ? "neutral-black-border+smaller-paper" : "smaller-paper";
            }
        }

        if (left == 0 && right == 0 && top == 0 && bottom == 0)
        {
            return null;
        }

        // Never permit a detector result to collapse the page. This should also protect against unexpected driver
        // pixel formats or pathological scans.
        int resultWidth = image.Width - left - right;
        int resultHeight = image.Height - top - bottom;
        if (resultWidth < image.Width * MIN_CONTENT_DIMENSION_FRACTION ||
            resultHeight < image.Height * MIN_CONTENT_DIMENSION_FRACTION)
        {
            return null;
        }

        var transform = new CropTransform(left, right, top, bottom, image.Width, image.Height);
        PageSize? detectedPageSize = null;
        if (image.HorizontalResolution > 0 && image.VerticalResolution > 0)
        {
            detectedPageSize = new PageSize(
                (decimal) (resultWidth / image.HorizontalResolution),
                (decimal) (resultHeight / image.VerticalResolution),
                PageSizeUnit.Inch);
        }
        return new SafeAutoCropResult(transform, detectedPageSize, reason);
    }

    private static CropAmounts DetectNeutralBlackBorders(RgbPixelReader reader, int width, int height,
        float horizontalResolution, float verticalResolution)
    {
        int maxX = GetBorderSearchPixels(width, horizontalResolution);
        int maxY = GetBorderSearchPixels(height, verticalResolution);
        int stepX = Math.Max(1, width / MAX_SAMPLES_PER_DIMENSION);
        int stepY = Math.Max(1, height / MAX_SAMPLES_PER_DIMENSION);

        int left = FindVerticalBorder(reader, width, height, 0, maxX, 1, stepY);
        int rightIndex = FindVerticalBorder(reader, width, height, width - 1, width - 1 - maxX, -1, stepY);
        int right = rightIndex < width ? width - 1 - rightIndex : 0;
        int top = FindHorizontalBorder(reader, width, height, 0, maxY, 1, stepX);
        int bottomIndex = FindHorizontalBorder(reader, width, height, height - 1, height - 1 - maxY, -1, stepX);
        int bottom = bottomIndex < height ? height - 1 - bottomIndex : 0;

        return new CropAmounts(left, right, top, bottom);
    }

    private static int FindVerticalBorder(RgbPixelReader reader, int width, int height, int start, int end,
        int direction, int sampleStep)
    {
        int lastBorder = -1;
        int allowedGap = 2;
        int gap = 0;
        for (int x = start; direction > 0 ? x <= end : x >= end; x += direction)
        {
            int nearBlack = 0;
            int samples = 0;
            int yStart = Math.Max(1, height / 100);
            int yEnd = Math.Min(height - 1, height - yStart);
            for (int y = yStart; y < yEnd; y += sampleStep)
            {
                if (IsNeutralBlack(reader[y, x])) nearBlack++;
                samples++;
            }

            if (samples > 0 && nearBlack / (double) samples >= 0.55)
            {
                lastBorder = x;
                gap = 0;
            }
            else if (lastBorder >= 0 && ++gap > allowedGap)
            {
                break;
            }
        }

        if (lastBorder < 0) return direction > 0 ? 0 : width;
        return direction > 0 ? Math.Min(width - 1, lastBorder + 1) : Math.Max(0, lastBorder - 1);
    }

    private static int FindHorizontalBorder(RgbPixelReader reader, int width, int height, int start, int end,
        int direction, int sampleStep)
    {
        int lastBorder = -1;
        int allowedGap = 2;
        int gap = 0;
        for (int y = start; direction > 0 ? y <= end : y >= end; y += direction)
        {
            int nearBlack = 0;
            int samples = 0;
            int xStart = Math.Max(1, width / 100);
            int xEnd = Math.Min(width - 1, width - xStart);
            for (int x = xStart; x < xEnd; x += sampleStep)
            {
                if (IsNeutralBlack(reader[y, x])) nearBlack++;
                samples++;
            }

            if (samples > 0 && nearBlack / (double) samples >= 0.55)
            {
                lastBorder = y;
                gap = 0;
            }
            else if (lastBorder >= 0 && ++gap > allowedGap)
            {
                break;
            }
        }

        if (lastBorder < 0) return direction > 0 ? 0 : height;
        return direction > 0 ? Math.Min(height - 1, lastBorder + 1) : Math.Max(0, lastBorder - 1);
    }

    private static CropAmounts DetectClearlySmallerContent(RgbPixelReader reader, int width, int height,
        CropAmounts border, float horizontalResolution, float verticalResolution)
    {
        int x0 = border.Left;
        int x1 = width - border.Right;
        int y0 = border.Top;
        int y1 = height - border.Bottom;
        if (x1 - x0 < 100 || y1 - y0 < 100)
        {
            return CropAmounts.None;
        }

        var background = EstimateBackground(reader, x0, x1, y0, y1);
        int stepX = Math.Max(1, (x1 - x0) / MAX_SAMPLES_PER_DIMENSION);
        int stepY = Math.Max(1, (y1 - y0) / MAX_SAMPLES_PER_DIMENSION);
        int[] columnMatches = new int[(x1 - x0 + stepX - 1) / stepX];
        int[] rowMatches = new int[(y1 - y0 + stepY - 1) / stepY];
        int totalMatches = 0;

        int rowIndex = 0;
        for (int y = y0; y < y1; y += stepY, rowIndex++)
        {
            int columnIndex = 0;
            for (int x = x0; x < x1; x += stepX, columnIndex++)
            {
                if (!IsContent(reader[y, x], background)) continue;
                rowMatches[rowIndex]++;
                columnMatches[columnIndex]++;
                totalMatches++;
            }
        }

        int minRowMatches = Math.Max(2, (int) Math.Ceiling(columnMatches.Length * CONTENT_LINE_COVERAGE_FRACTION));
        int minColumnMatches = Math.Max(2, (int) Math.Ceiling(rowMatches.Length * CONTENT_LINE_COVERAGE_FRACTION));
        int firstRow = FindFirstStable(rowMatches, minRowMatches);
        int lastRow = FindLastStable(rowMatches, minRowMatches);
        int firstColumn = FindFirstStable(columnMatches, minColumnMatches);
        int lastColumn = FindLastStable(columnMatches, minColumnMatches);
        if (firstRow < 0 || lastRow < firstRow || firstColumn < 0 || lastColumn < firstColumn)
        {
            return CropAmounts.None;
        }

        int marginX = GetSafetyMarginPixels(width, horizontalResolution);
        int marginY = GetSafetyMarginPixels(height, verticalResolution);
        int contentLeft = Math.Max(x0, x0 + firstColumn * stepX - marginX);
        int contentRightExclusive = Math.Min(x1, x0 + (lastColumn + 1) * stepX + marginX);
        int contentTop = Math.Max(y0, y0 + firstRow * stepY - marginY);
        int contentBottomExclusive = Math.Min(y1, y0 + (lastRow + 1) * stepY + marginY);

        double resultWidthFraction = (contentRightExclusive - contentLeft) / (double) width;
        double resultHeightFraction = (contentBottomExclusive - contentTop) / (double) height;
        double resultAreaFraction = resultWidthFraction * resultHeightFraction;
        int sampledContentWidth = lastColumn - firstColumn + 1;
        int sampledContentHeight = lastRow - firstRow + 1;
        double foregroundDensity = totalMatches / (double) (sampledContentWidth * sampledContentHeight);

        // An ordinary A4 document often has text occupying 75-85% of the page. Cropping that text box would remove
        // legitimate margins, so require a substantially smaller object and either a visually solid paper region
        // (coloured/aged sheets) or a very small bounding box (cards/receipts) before treating it as a smaller sheet.
        bool atLeastOneDimensionClearlySmaller =
            resultWidthFraction <= MAX_CONTENT_RESULT_DIMENSION_FRACTION ||
            resultHeightFraction <= MAX_CONTENT_RESULT_DIMENSION_FRACTION;
        bool clearlySmaller = resultAreaFraction <= MAX_CONTENT_RESULT_AREA_FRACTION &&
                              resultWidthFraction <= MAX_UNCHANGED_DIMENSION_FRACTION &&
                              resultHeightFraction <= MAX_UNCHANGED_DIMENSION_FRACTION &&
                              atLeastOneDimensionClearlySmaller &&
                              resultWidthFraction >= MIN_CONTENT_DIMENSION_FRACTION &&
                              resultHeightFraction >= MIN_CONTENT_DIMENSION_FRACTION &&
                              (foregroundDensity >= 0.08 || resultAreaFraction <= 0.45);
        if (!clearlySmaller)
        {
            return CropAmounts.None;
        }

        return new CropAmounts(contentLeft, width - contentRightExclusive, contentTop, height - contentBottomExclusive);
    }

    private static (int r, int g, int b) EstimateBackground(RgbPixelReader reader, int x0, int x1, int y0, int y1)
    {
        var samples = new List<(int r, int g, int b)>();
        int patchWidth = Math.Max(2, (x1 - x0) / 50);
        int patchHeight = Math.Max(2, (y1 - y0) / 50);
        int stepX = Math.Max(1, patchWidth / 4);
        int stepY = Math.Max(1, patchHeight / 4);

        AddPatch(x0, y0);
        AddPatch(Math.Max(x0, x1 - patchWidth), y0);
        AddPatch(x0, Math.Max(y0, y1 - patchHeight));
        AddPatch(Math.Max(x0, x1 - patchWidth), Math.Max(y0, y1 - patchHeight));

        int Median(Func<(int r, int g, int b), int> selector)
        {
            var values = samples.Select(selector).OrderBy(x => x).ToArray();
            return values[values.Length / 2];
        }

        return (Median(x => x.r), Median(x => x.g), Median(x => x.b));

        void AddPatch(int startX, int startY)
        {
            for (int y = startY; y < Math.Min(y1, startY + patchHeight); y += stepY)
            {
                for (int x = startX; x < Math.Min(x1, startX + patchWidth); x += stepX)
                {
                    samples.Add(reader[y, x]);
                }
            }
        }
    }

    private static int FindFirstStable(int[] matches, int threshold)
    {
        for (int i = 0; i < matches.Length; i++)
        {
            int hits = 0;
            for (int j = i; j < Math.Min(matches.Length, i + 3); j++)
            {
                if (matches[j] >= threshold) hits++;
            }
            if (hits >= 2) return i;
        }
        return -1;
    }

    private static int FindLastStable(int[] matches, int threshold)
    {
        for (int i = matches.Length - 1; i >= 0; i--)
        {
            int hits = 0;
            for (int j = i; j >= Math.Max(0, i - 2); j--)
            {
                if (matches[j] >= threshold) hits++;
            }
            if (hits >= 2) return i;
        }
        return -1;
    }

    private static bool MatchesFallbackCanvas(IMemoryImage image, PageSize? fallbackPageSize)
    {
        if (fallbackPageSize == null || image.HorizontalResolution <= 0 || image.VerticalResolution <= 0)
        {
            return false;
        }

        double widthInches = image.Width / image.HorizontalResolution;
        double heightInches = image.Height / image.VerticalResolution;
        double targetWidth = (double) fallbackPageSize.WidthInInches;
        double targetHeight = (double) fallbackPageSize.HeightInInches;
        return NearlyEqual(widthInches, targetWidth) && NearlyEqual(heightInches, targetHeight) ||
               NearlyEqual(widthInches, targetHeight) && NearlyEqual(heightInches, targetWidth);
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= PAGE_SIZE_TOLERANCE_INCHES;

    private static int GetBorderSearchPixels(int dimension, float resolution)
    {
        int byFraction = Math.Max(2, (int) Math.Round(dimension * MAX_BORDER_SEARCH_FRACTION));
        if (resolution <= 0) return byFraction;
        int fourMillimetres = (int) Math.Round(resolution * 4.0 / 25.4);
        return Math.Min(byFraction, Math.Max(2, fourMillimetres));
    }

    private static int GetSafetyMarginPixels(int dimension, float resolution)
    {
        if (resolution > 0)
        {
            return Math.Max(2, (int) Math.Round(resolution * SAFETY_MARGIN_INCHES));
        }
        return Math.Max(2, (int) Math.Round(dimension * 0.012));
    }

    private static bool IsNeutralBlack((int r, int g, int b) color)
    {
        int max = Math.Max(color.r, Math.Max(color.g, color.b));
        int min = Math.Min(color.r, Math.Min(color.g, color.b));
        return max <= 72 && max - min <= 24;
    }

    private static bool IsContent((int r, int g, int b) color, (int r, int g, int b) background)
    {
        int colorDistance = Math.Max(Math.Abs(color.r - background.r),
            Math.Max(Math.Abs(color.g - background.g), Math.Abs(color.b - background.b)));
        int luma = (color.r * 299 + color.g * 587 + color.b * 114) / 1000;
        int backgroundLuma = (background.r * 299 + background.g * 587 + background.b * 114) / 1000;
        return colorDistance >= CONTENT_COLOR_DISTANCE || luma <= backgroundLuma - CONTENT_LUMA_DISTANCE;
    }

    private readonly record struct CropAmounts(int Left, int Right, int Top, int Bottom)
    {
        public static CropAmounts None => new(0, 0, 0, 0);
        public bool HasCrop => Left > 0 || Right > 0 || Top > 0 || Bottom > 0;
    }
}

internal record SafeAutoCropResult(CropTransform Transform, PageSize? DetectedPageSize, string Reason);
