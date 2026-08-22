namespace NAPS2.Images;

// TODO: Write tests for this class
/// <summary>
/// A mutable container for an image in the NAPS2 UI that can be edited, has a thumbnail, etc.
/// </summary>
public class UiImage : IDisposable
{
    private ProcessedImage _processedImage;
    private IMemoryImage? _thumbnail;
    private TransformState? _thumbnailTransformState;
    private bool _saved;

    public UiImage(ProcessedImage image, bool useThumbnail = true)
    {
        _processedImage = image;
        var ppd = _processedImage.PostProcessingData;
        if (useThumbnail && ppd.Thumbnail != null && ppd.ThumbnailTransformState != null)
        {
            _thumbnail = _processedImage.PostProcessingData.Thumbnail;
            _thumbnailTransformState = _processedImage.PostProcessingData.ThumbnailTransformState;
        }
    }

    public ProcessedImage GetClonedImage()
    {
        lock (this)
        {
            return _processedImage.Clone();
        }
    }

    public ProcessedImage.WeakReference GetImageWeakReference()
    {
        lock (this)
        {
            return _processedImage.GetWeakReference();
        }
    }

    public void MarkSaved(ProcessedImage state)
    {
        lock (this)
        {
            if (Equals(state, _processedImage))
            {
                _saved = true;
            }
        }
    }

    public void Dispose()
    {
        lock (this)
        {
            if (IsDisposed)
            {
                return;
            }
            IsDisposed = true;
            _processedImage.Dispose();
            EditorSessions.DisposeAll();

            if (_thumbnail != null)
            {
                _thumbnail.Dispose();
                _thumbnail = null;
            }
        }
    }

    public void AddTransform(Transform transform, IMemoryImage? prerenderedThumbnail = null)
    {
        AddTransforms([transform], prerenderedThumbnail);
    }

    public void AddTransforms(IEnumerable<Transform> transforms, IMemoryImage? prerenderedThumbnail = null)
    {
        var transformList = transforms.ToList();
        if (transformList.All(x => x.IsNull))
        {
            prerenderedThumbnail?.Dispose();
            return;
        }
        lock (this)
        {
            foreach (var transform in transformList)
            {
                _processedImage = _processedImage.WithTransform(transform, true);
            }
            if (prerenderedThumbnail != null)
            {
                _thumbnail?.Dispose();
                _thumbnail = prerenderedThumbnail;
                _thumbnailTransformState = _processedImage.TransformState;
            }
            _saved = false;
        }
        ThumbnailInvalidated?.Invoke(this, EventArgs.Empty);
        if (prerenderedThumbnail != null)
        {
            ThumbnailChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ReplaceTransformState(TransformState oldTransformState, TransformState newTransformState)
    {
        lock (this)
        {
            if (TransformState != oldTransformState)
            {
                return;
            }
            _processedImage = _processedImage.WithTransformState(newTransformState, true);
            _saved = false;
        }
        ThumbnailInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceInternalImage(ProcessedImage newImage)
    {
        lock (this)
        {
            _processedImage.Dispose();
            _processedImage = newImage;
            _thumbnailTransformState = null;
            _saved = false;
        }
        ThumbnailInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public void ResetTransforms()
    {
        lock (this)
        {
            if (_processedImage.TransformState.IsEmpty)
            {
                return;
            }
            _processedImage = _processedImage.WithNoTransforms(true);
            _saved = false;
        }
        ThumbnailInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public IMemoryImage? GetThumbnailClone()
    {
        lock (this)
        {
            return _thumbnail?.Clone();
        }
    }

    public int GetThumbnailSize()
    {
        lock (this)
        {
            if (_thumbnail == null)
            {
                return -1;
            }
            return Math.Max(_thumbnail.Width, _thumbnail.Height);
        }
    }

    public void SetThumbnail(IMemoryImage image, TransformState transformState)
    {
        lock (this)
        {
            _thumbnail?.Dispose();
            _thumbnail = image;
            _thumbnailTransformState = transformState;
        }
        ThumbnailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Assigns or clears the CCP Party-member dossier document type for this page.
    /// This changes metadata only and never changes image pixels.
    /// </summary>
    public void SetPartyDossierDocumentType(int? documentTypeId)
    {
        if (documentTypeId is < 1 or > 104)
        {
            throw new ArgumentOutOfRangeException(nameof(documentTypeId));
        }

        lock (this)
        {
            var data = _processedImage.PostProcessingData with
            {
                PartyDossierDocumentTypeId = documentTypeId
            };
            _processedImage = _processedImage.WithPostProcessingData(data, true);
            _saved = false;
        }

        // Re-use the thumbnail refresh event so the list view immediately updates the HS xx label.
        ThumbnailChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsDisposed { get; private set; }

    public bool IsThumbnailDirty => _thumbnailTransformState != _processedImage.TransformState;

    public bool HasUnsavedChanges => !_saved;

    public bool IsBlankPageCandidate
    {
        get
        {
            lock (this)
            {
                return _processedImage.PostProcessingData.IsBlankPageCandidate;
            }
        }
    }

    public double BlankPageCoverage
    {
        get
        {
            lock (this)
            {
                return _processedImage.PostProcessingData.BlankPageCoverage;
            }
        }
    }

    public int? PartyDossierDocumentTypeId
    {
        get
        {
            lock (this)
            {
                return _processedImage.PostProcessingData.PartyDossierDocumentTypeId;
            }
        }
    }

    public TransformState TransformState => _processedImage.TransformState;

    public List<ExternalEditorSession> EditorSessions { get; } = new();

    public EventHandler? ThumbnailChanged;

    public EventHandler? ThumbnailInvalidated;

    public ImageRenderState GetImageRenderState()
    {
        lock (this)
        {
            return new ImageRenderState(_processedImage.GetWeakReference(), _thumbnailTransformState, _thumbnail, this);
        }
    }
}