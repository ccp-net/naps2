namespace NAPS2.Pdf;

/// <summary>
/// Additional parameters for exporting PDFs (metadata, encryption, compatibility).
/// </summary>
public record PdfExportParams
{
    public PdfExportParams()
    {
    }

    public PdfExportParams(PdfMetadata metadata, PdfEncryption encryption, PdfCompat compat)
    {
        Metadata = metadata;
        Encryption = encryption;
        Compat = compat;
    }

    public PdfMetadata Metadata { get; init; } = new();

    public PdfEncryption Encryption { get; init; } = new();
    
    public PdfCompat Compat { get; init; } = PdfCompat.Default;

    /// <summary>
    /// Optional maximum size, in bytes, for the JPEG image embedded for each raster page.
    /// A null value keeps the normal NAPS2 behavior. CCP Scan uses this to satisfy its per-page upload limit while
    /// preserving the original pixel dimensions first and only downscaling when JPEG quality reduction is insufficient.
    /// </summary>
    public long? MaxImageBytes { get; init; }

    /// <summary>
    /// Optional per-page export overrides. A null list keeps normal NAPS2 export behavior for every page; a null
    /// element keeps normal behavior for that specific page. CCP Scan uses this to leave compliant pages untouched
    /// while selectively reducing only pages that exceed the upload-size budget.
    /// </summary>
    public IReadOnlyList<PdfPageExportOptions?>? PageOptions { get; init; }
}

/// <summary>
/// Per-page raster export controls used by CCP Scan's size optimizer.
/// </summary>
public record PdfPageExportOptions
{
    public int? TargetDpi { get; init; }
    public int? JpegQuality { get; init; }
}