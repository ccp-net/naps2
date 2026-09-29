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
    /// Optional per-page export overrides. CCP Scan v0.2.14 uses these to lock the chosen effective DPI and JPEG
    /// quality after measuring each page as a real one-page PDF. When null, normal NAPS2 export behavior is used.
    /// </summary>
    public IReadOnlyList<PdfPageExportOptions>? PageOptions { get; init; }
}

/// <summary>
/// Per-page raster export controls used by CCP Scan's size optimizer.
/// </summary>
public record PdfPageExportOptions
{
    public int? TargetDpi { get; init; }
    public int? JpegQuality { get; init; }
}