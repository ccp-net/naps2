using NAPS2.Images.Transforms;
using NAPS2.ImportExport;
using NAPS2.ImportExport.Email;
using NAPS2.Ocr;

namespace NAPS2.Pdf;

internal class SavePdfOperation : OperationBase
{
    // CCP Scan target is deliberately a little below 20 MiB so the resulting file remains under the user's 20 MB
    // operational limit even when file-size displays round differently.
    private const long CCP_TARGET_PDF_BYTES = 19L * 1024 * 1024 + 512L * 1024;

    // 300 dpi source pages become roughly 270/240/210/180/150/120 effective dpi. We stop as soon as the target is met.
    private static readonly double[] CCP_REDUCTION_SCALES = { 0.90, 0.80, 0.70, 0.60, 0.50, 0.40 };

    private readonly PdfExporter _pdfExporter;
    private readonly IOverwritePrompt _overwritePrompt;
    private readonly IEmailProviderFactory? _emailProviderFactory;

    public SavePdfOperation(PdfExporter pdfExporter, IOverwritePrompt overwritePrompt,
        IEmailProviderFactory? emailProviderFactory = null)
    {
        _pdfExporter = pdfExporter;
        _overwritePrompt = overwritePrompt;
        _emailProviderFactory = emailProviderFactory;

        AllowCancel = true;
        AllowBackground = true;
    }

    // TODO: Do something with this re: notifications?
    public string? FirstFileSaved { get; private set; }

    public bool Start(string fileName, Placeholders placeholders, ICollection<ProcessedImage> images,
        PdfSettings pdfSettings, OcrParams ocrParams, EmailMessage? emailMessage = null,
        string? overwriteFile = null)
    {
        // TODO: This needs tests. And ideally simplification.
        ProgressTitle = emailMessage != null ? MiscResources.EmailPdfProgress : MiscResources.SavePdfProgress;
        var subFileName = placeholders.Substitute(fileName);
        Status = new OperationStatus
        {
            StatusText = string.Format(MiscResources.SavingFormat, Path.GetFileName(subFileName)),
            MaxProgress = images.Count
        };

        if (Directory.Exists(subFileName))
        {
            // Not supposed to be a directory, but ok...
            subFileName = placeholders.Substitute(Path.Combine(subFileName, "$(n).pdf"));
        }
        var singleFile = !pdfSettings.SinglePagePdfs || images.Count == 1;
        if (singleFile)
        {
            if (File.Exists(subFileName))
            {
                if (subFileName != overwriteFile &&
                    _overwritePrompt.ConfirmOverwrite(subFileName) != OverwriteResponse.Yes)
                {
                    FailedToStart();
                    return false;
                }
                if (FileSystemHelper.IsFileInUse(subFileName, out var ex))
                {
                    InvokeError(MiscResources.FileInUse, ex!);
                    FailedToStart();
                    return false;
                }
            }
        }

        var imagesByFile = pdfSettings.SinglePagePdfs
            ? images.Select(x => new[] { x }).ToArray()
            : new[] { images.ToArray() };
        RunAsync(async () =>
        {
            bool result = false;
            try
            {
                int digits = (int) Math.Floor(Math.Log10(images.Count)) + 1;
                int i = 0;
                foreach (var imagesForFile in imagesByFile)
                {
                    var currentFileName = placeholders.Substitute(fileName, true, i, singleFile ? 0 : digits);
                    // TODO: Overwrite prompt non-single file?
                    Status.StatusText = string.Format(MiscResources.SavingFormat, Path.GetFileName(currentFileName));
                    InvokeStatusChanged();
                    if (singleFile && IsFileInUse(currentFileName, out var ex))
                    {
                        InvokeError(MiscResources.FileInUse, ex!);
                        break;
                    }

                    var progress = new ProgressHandler(singleFile ? OnProgress : null, CancelToken);
                    result = await _pdfExporter.Export(currentFileName, imagesForFile,
                        new PdfExportParams(pdfSettings.Metadata, pdfSettings.Encryption,
                            pdfSettings.Compat), ocrParams, progress);
                    if (!result || CancelToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // CCP fast workflow: keep normal PDF quality for ordinary files. Only files over the 20 MB limit
                    // are re-exported. Scaling forces scan images to be re-encoded and reduces both pixel count and PDF
                    // size. The first scale that meets the target wins, preserving as much quality as possible.
                    if (emailMessage == null && File.Exists(currentFileName) &&
                        new FileInfo(currentFileName).Length > CCP_TARGET_PDF_BYTES)
                    {
                        Status.StatusText = $"Đang giảm dung lượng {Path.GetFileName(currentFileName)} xuống dưới 20 MB...";
                        InvokeStatusChanged();
                        result = await ReducePdfSize(currentFileName, imagesForFile, pdfSettings, ocrParams);
                        if (!result || CancelToken.IsCancellationRequested)
                        {
                            break;
                        }
                    }

                    emailMessage?.Attachments.Add(new EmailAttachment
                    {
                        FilePath = currentFileName,
                        AttachmentName = Path.GetFileName(currentFileName)
                    });
                    if (i == 0)
                    {
                        FirstFileSaved = subFileName;
                    }
                    i++;
                    if (!singleFile)
                    {
                        OnProgress(i, imagesByFile.Length);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                InvokeError(MiscResources.DontHavePermission, ex);
            }
            catch (Exception ex)
            {
                Log.ErrorException(MiscResources.ErrorSaving, ex);
                InvokeError(MiscResources.ErrorSaving, ex);
            }
            finally
            {
                GC.Collect();
            }

            if (result && emailMessage != null && _emailProviderFactory != null)
            {
                Status.StatusText = MiscResources.UploadingEmail;
                Status.CurrentProgress = 0;
                Status.MaxProgress = 1;
                Status.ProgressType = OperationProgressType.MB;
                InvokeStatusChanged();

                try
                {
                    result = await _emailProviderFactory.Default.SendEmail(emailMessage, ProgressHandler);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.ErrorException(MiscResources.ErrorEmailing, ex);
                    InvokeError(MiscResources.ErrorEmailing, ex);
                }
            }

            return result;
        });
        Success.ContinueWith(task =>
        {
            if (task.Result)
            {
                if (emailMessage != null)
                {
                    Log.Event(EventType.Email, new EventParams
                    {
                        Name = MiscResources.EmailPdf,
                        Pages = images.Count,
                        FileFormat = ".pdf"
                    });
                }
                else
                {
                    Log.Event(EventType.SavePdf, new EventParams
                    {
                        Name = MiscResources.SavePdf,
                        Pages = images.Count,
                        FileFormat = ".pdf"
                    });
                }
            }
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        return true;
    }

    private async Task<bool> ReducePdfSize(string fileName, ICollection<ProcessedImage> images,
        PdfSettings pdfSettings, OcrParams ocrParams)
    {
        long bestSize = new FileInfo(fileName).Length;
        var tempFile = Path.Combine(Path.GetDirectoryName(fileName) ?? Paths.Temp,
            $".{Path.GetFileNameWithoutExtension(fileName)}.ccp-compress-{Guid.NewGuid():N}.pdf");

        try
        {
            foreach (var scale in CCP_REDUCTION_SCALES)
            {
                if (CancelToken.IsCancellationRequested)
                {
                    return false;
                }

                var scaledImages = images.Select(x => x.WithTransform(new ScaleTransform(scale))).ToList();
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }

                    var compressionProgress = new ProgressHandler(null, CancelToken);
                    var success = await _pdfExporter.Export(tempFile, scaledImages,
                        new PdfExportParams(pdfSettings.Metadata, pdfSettings.Encryption, pdfSettings.Compat),
                        ocrParams, compressionProgress);
                    if (!success || !File.Exists(tempFile))
                    {
                        continue;
                    }

                    var candidateSize = new FileInfo(tempFile).Length;
                    if (candidateSize < bestSize)
                    {
                        File.Copy(tempFile, fileName, true);
                        bestSize = candidateSize;
                    }

                    if (bestSize <= CCP_TARGET_PDF_BYTES)
                    {
                        Log.Info($"CCP PDF size reduction completed: {bestSize / 1024.0 / 1024.0:F1} MB at scale {scale:P0}.");
                        return true;
                    }
                }
                finally
                {
                    foreach (var image in scaledImages)
                    {
                        image.Dispose();
                    }
                }
            }

            // Keep the smallest successfully generated PDF even if an unusually large dossier cannot reach the target
            // without dropping below the 120 dpi floor. This avoids silently destroying legibility.
            Log.Info($"CCP PDF size reduction reached minimum scale; final size {bestSize / 1024.0 / 1024.0:F1} MB.");
            return true;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
            }
        }
    }

    private bool IsFileInUse(string filePath, out Exception? exception)
    {
        // TODO: Generalize this for images too
        exception = null;
        if (File.Exists(filePath))
        {
            try
            {
                using (new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
            }
            catch (IOException ex)
            {
                exception = ex;
                return true;
            }
        }
        return false;
    }
}