using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAPS2.Ocr;
using NAPS2.Remoting.Worker;

namespace NAPS2.Scan;

/// <summary>
/// A ScanningContext object is needed for most NAPS2 operations. Set it up with the corresponding ImageContext type
/// for image type you expect (e.g. GdiImageContext for System.Drawing.Bitmap, if you're using Windows Forms). You can
/// also set various other properties that affect scanning and image processing.
/// <para/>
/// When the ScanningContext is disposed, all ProcessedImage objects that were generating from scanning or importing
/// with that ScanningContext object will be automatically disposed.
/// </summary>
public class ScanningContext : IDisposable
{
    private readonly ProcessedImageOwner _processedImageOwner = new();

    /// <summary>
    /// Initializes a new instance of the ScanningContext class with the specified ImageContext.
    /// </summary>
    /// <param name="imageContext">The corresponding ImageContext type used for images.</param>
    public ScanningContext(ImageContext imageContext)
    {
        ImageContext = imageContext;
    }

    public ImageContext ImageContext { get; }

    public FileStorageManager? FileStorageManager { get; set; }

    internal IWorkerFactory? WorkerFactory { get; set; }

    public IOcrEngine? OcrEngine { get; set; }

    /// <summary>
    /// Base folder containing Tesseract language data subfolders (normally "fast" and "best").
    /// Used by CCP Auto Orientation to run lightweight layout analysis without enabling OCR output in the UI.
    /// </summary>
    public string? OcrLanguageDataPath { get; set; }

    public string TempFolderPath { get; set; } = Path.GetTempPath();

    public ILogger Logger { get; set; } = NullLogger.Instance;

    internal string? RecoveryPath { get; set; }

    internal OcrRequestQueue OcrRequestQueue { get; } = new();

    public void Dispose()
    {
        _processedImageOwner.Dispose();
        FileStorageManager?.Dispose();
    }

    internal WorkerContext? CreateWorker(WorkerType workerType)
    {
        return WorkerFactory?.Create(this, workerType);
    }

    internal ProcessedImage CreateProcessedImage(IImageStorage storage, bool lossless = false, int quality = -1,
        PageSize? pageSize = null, IEnumerable<Transform>? transforms = null,
        RefCount? refCount = null)
    {
        var convertedStorage = ConvertStorageIfNeeded(storage, lossless, quality);
        var metadata = new ImageMetadata(lossless, pageSize);
        var postProcessingData = new PostProcessingData();
        refCount ??=
            new RefCount(
                new ProcessedImage.InternalDisposer(convertedStorage, postProcessingData, _processedImageOwner));
        var image = new ProcessedImage(
            ImageContext,
            convertedStorage,
            metadata,
            postProcessingData,
            new TransformState(transforms?.ToImmutableList() ?? []),
            refCount);
        return image;
    }

    private IImageStorage ConvertStorageIfNeeded(IImageStorage storage, bool lossless, int quality)
    {
        if (FileStorageManager != null)
        {
            return ConvertToFileStorage(storage, lossless, quality);
        }
        return ConvertToMemoryStorage(storage);
    }

    private IImageStorage ConvertToMemoryStorage(IImageStorage storage)
    {
        switch (storage)
        {
            case IMemoryImage image:
                return image.Clone();
            case ImageFileStorage fileStorage:
                return ImageContext.Load(fileStorage.FullPath);
            case ImageMemoryStorage memoryStorage:
                if (memoryStorage.TypeHint == ".pdf")
                {
                    return memoryStorage;
                }
                return ImageContext.Load(memoryStorage.Stream);
            default:
                return storage;
        }
    }

    private IImageStorage ConvertToFileStorage(IImageStorage storage, bool lossless, int quality)
    {
        switch (storage)
        {
            case IMemoryImage image:
                return WriteImageToBackingFile(image, lossless, quality);
            case ImageFileStorage fileStorage:
                return fileStorage;
            case ImageMemoryStorage memoryStorage:
                if (memoryStorage.TypeHint == ".pdf")
                {
                    return WriteDataToBackingFile(memoryStorage.Stream, ".pdf");
                }
                var loadedImage = ImageContext.Load(memoryStorage.Stream);
                return WriteImageToBackingFile(loadedImage, lossless, quality);
            default:
                return storage;
        }
    }

    private ImageFileStorage WriteDataToBackingFile(MemoryStream stream, string ext)
    {
        if (FileStorageManager == null)
        {
            throw new InvalidOperationException();
        }
        var path = FileStorageManager.NextFilePath() + ext;
        using var fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        stream.WriteTo(fileStream);
        return new ImageFileStorage(path, false);
    }

    private IImageStorage WriteImageToBackingFile(IMemoryImage image, bool lossless, int quality)
    {
        if (FileStorageManager == null)
        {
            throw new InvalidOperationException();
        }
        var path = FileStorageManager.NextFilePath();
        var fullPath = ImageExportHelper.SaveSmallestFormat(path, image, lossless, quality, out _);
        return new ImageFileStorage(fullPath, false);
    }

    internal string SaveToTempFile(IMemoryImage image)
    {
        var path = Path.Combine(TempFolderPath, Path.GetRandomFileName());
        return ImageExportHelper.SaveSmallestFormat(path, image, false, -1, out _);
    }

    internal string SaveToTempFile(ProcessedImage image)
    {
        using var rendered = image.Render();
        return SaveToTempFile(rendered);
    }

    private class ProcessedImageOwner : IProcessedImageOwner, IDisposable
    {
        private readonly HashSet<IDisposable> _disposables = new HashSet<IDisposable>();

        public void Register(IDisposable internalDisposable)
        {
            lock (this)
            {
                _disposables.Add(internalDisposable);
            }
        }

        public void Unregister(IDisposable internalDisposable)
        {
            lock (this)
            {
                _disposables.Remove(internalDisposable);
            }
        }

        public void Dispose()
        {
            IEnumerable<IDisposable> list;
            lock (this)
            {
                list = _disposables.ToList();
            }
            foreach (var disposable in list)
            {
                disposable.Dispose();
            }
        }
    }
}