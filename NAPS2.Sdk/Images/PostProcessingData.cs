using System.Threading;

namespace NAPS2.Images;

/// <summary>
/// Represents information about an image obtained during post-processing (e.g. thumbnail image, barcode).
/// </summary>
public record PostProcessingData(
    IMemoryImage? Thumbnail,
    TransformState? ThumbnailTransformState,
    int PageNumber,
    PageSide PageSide,
    Barcode Barcode,
    CancellationTokenSource? OcrCts,
    string? OriginalFilePath)
{
    public PostProcessingData() : this(null, null, 0, PageSide.Unknown, Barcode.NoDetection, null, null)
    {
    }

    /// <summary>
    /// True when blank-page analysis considers this page a likely blank separator.
    /// This is advisory QC metadata only; the page is not removed when blank-page exclusion is disabled.
    /// </summary>
    public bool IsBlankPageCandidate { get; init; }

    /// <summary>
    /// Fraction of pixels classified as non-white by blank-page analysis.
    /// Lower values indicate a page that is more likely to be blank.
    /// </summary>
    public double BlankPageCoverage { get; init; }

    /// <summary>
    /// CCP v0.2 Party-member dossier document type (01-104) assigned by the operator.
    /// Null means the page has not yet been classified.
    /// </summary>
    public int? PartyDossierDocumentTypeId { get; init; }
}