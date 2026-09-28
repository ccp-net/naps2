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
}