using Eto.Drawing;
using Eto.Forms;
using Google.Protobuf;
using NAPS2.ImportExport.Images;

namespace NAPS2.EtoForms.Widgets;

public class ImageListViewBehavior : ListViewBehavior<UiImage>
{
    private const int QC_BORDER_WIDTH = 4;
    private static readonly Color BlankQcBorderColor = new(1.0f, 0.72f, 0.0f);
    private static readonly Color DarkQcBorderColor = new(0.88f, 0.10f, 0.10f);

    private readonly UiThumbnailProvider _thumbnailProvider;
    private readonly Naps2Config _config;
    private readonly ImageTransfer _imageTransfer = new();

    public ImageListViewBehavior(UiThumbnailProvider thumbnailProvider,
        ColorScheme colorScheme, Naps2Config config) : base(colorScheme)
    {
        _thumbnailProvider = thumbnailProvider;
        _config = config;
        MultiSelect = true;
        ShowLabels = false;
        ScrollOnDrag = true;
        UseHandCursor = true;
    }

    public override bool ShowPageNumbers => _config.Get(c => c.ShowPageNumbers);

    public override Image GetImage(IListView<UiImage> listView, UiImage item)
    {
        using var thumbnail = _thumbnailProvider.GetThumbnail(item, listView.ImageSize.Width);
        var image = thumbnail.ToEtoImage();

        Color? warningColor = item.IsDarkPageCandidate
            ? DarkQcBorderColor
            : item.IsBlankPageCandidate
                ? BlankQcBorderColor
                : null;
        if (warningColor == null)
        {
            return image;
        }

        var highlightedImage = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppRgba);
        using (var graphics = new Graphics(highlightedImage))
        using (var borderPen = new Pen(warningColor.Value, QC_BORDER_WIDTH))
        {
            graphics.Clear(Colors.Transparent);
            graphics.DrawImage(image, 0, 0);
            var inset = QC_BORDER_WIDTH / 2f;
            graphics.DrawRectangle(borderPen, inset, inset,
                Math.Max(1, image.Width - QC_BORDER_WIDTH),
                Math.Max(1, image.Height - QC_BORDER_WIDTH));
        }
        image.Dispose();
        return highlightedImage;
    }

    public override bool AllowDragDrop => true;

    public override bool AllowFileDrop => true;

    public override string CustomDragDataType => _imageTransfer.TypeName;

    public override byte[] SerializeCustomDragData(UiImage[] items)
    {
        using var processedImages = items.Select(x => x.GetClonedImage()).ToDisposableList();
        return _imageTransfer.ToBinaryData(processedImages);
    }

    public override DragEffects GetCustomDragEffect(byte[] data)
    {
        var dataObj = _imageTransfer.FromBinaryData(data);
        return dataObj.ProcessId == Process.GetCurrentProcess().Id
            ? DragEffects.Move
            : DragEffects.Copy;
    }

    public override byte[] MergeCustomDragData(byte[][] dataItems)
    {
        var mergedObj = new ImageTransferData();
        foreach (var data in dataItems)
        {
            var dataObj = _imageTransfer.FromBinaryData(data);
            if (mergedObj.ProcessId != 0 && mergedObj.ProcessId != dataObj.ProcessId)
            {
                throw new ArgumentException("Inconsistent process IDs in drag data");
            }
            mergedObj.ProcessId = dataObj.ProcessId;
            mergedObj.SerializedImages.AddRange(dataObj.SerializedImages);
        }
        return mergedObj.ToByteArray();
    }
}