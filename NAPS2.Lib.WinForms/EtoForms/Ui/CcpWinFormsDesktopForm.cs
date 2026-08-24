using Eto.Forms;
using Eto.WinForms;
using NAPS2.EtoForms.Desktop;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Notifications;
using NAPS2.EtoForms.Widgets;
using NAPS2.EtoForms.WinForms;
using NAPS2.ImportExport.Images;
using NAPS2.Scan;
using NAPS2.Util;
using NAPS2.WinForms;
using WF = System.Windows.Forms;

namespace NAPS2.EtoForms.Ui;

/// <summary>
/// Windows desktop shell customized for the CCP Party-member dossier scanning workflow.
/// </summary>
public class CcpWinFormsDesktopForm : WinFormsDesktopForm
{
    private const string APP_NAME = "CCP SCAN HỒ SƠ ĐẢNG VIÊN";
    private const string APP_VERSION = "v0.2.3 Legacy Paper Safe Scan Preview";
    private const string APP_AUTHOR = "Chế Công Phước";

    private readonly UiImageList _imageList;
    private readonly DesktopController _desktopController;
    private WF.Label? _sessionStatusLabel;
    private WF.Timer? _statusTimer;

    public CcpWinFormsDesktopForm(
        Naps2Config config,
        DesktopKeyboardShortcuts keyboardShortcuts,
        NotificationManager notificationManager,
        CultureHelper cultureHelper,
        ColorScheme colorScheme,
        IProfileManager profileManager,
        UiImageList imageList,
        ThumbnailController thumbnailController,
        UiThumbnailProvider thumbnailProvider,
        DesktopController desktopController,
        IDesktopScanController desktopScanController,
        ImageListActions imageListActions,
        ImageListViewBehavior imageListViewBehavior,
        DesktopFormProvider desktopFormProvider,
        IDesktopSubFormController desktopSubFormController,
        Lazy<DesktopCommands> commands,
        Sidebar sidebar,
        IIconProvider iconProvider)
        : base(config, keyboardShortcuts, notificationManager, cultureHelper, colorScheme, profileManager, imageList,
            thumbnailController, thumbnailProvider, desktopController, desktopScanController, imageListActions,
            imageListViewBehavior, desktopFormProvider, desktopSubFormController, commands, sidebar, iconProvider)
    {
        _imageList = imageList;
        _desktopController = desktopController;
        ApplyCcpVietnameseLabels();
    }

    private void ApplyCcpVietnameseLabels()
    {
        if (!string.Equals(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "vi",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Commands.Profiles.Text = "Cấu hình";
        Commands.Settings.Text = "Cài đặt";
        Commands.Import.Text = "Nhập";
        Commands.SaveAndNewDossier.Text = "Lưu & Hồ sơ mới";
    }

    protected override void CreateToolbarMenu(Command command, MenuProvider menu)
    {
        if (ReferenceEquals(command, Commands.RotateMenu))
        {
            CreateToolbarStackedButtons(Commands.RotateLeft, Commands.RotateRight);
            CreateToolbarButton(Commands.Flip);
            return;
        }

        base.CreateToolbarMenu(command, menu);
    }

    protected override void BuildLayout()
    {
        base.BuildLayout();

        var splitter = ((LayoutLeftPanel) LayoutController.Content!).Splitter;
        var sidebarPanel = (WF.Panel) splitter.Panel1.ToNative();

        var saveAndNewButton = new WF.Button
        {
            AutoSize = false,
            Dock = WF.DockStyle.Bottom,
            Height = 34,
            Margin = new WF.Padding(12, 3, 12, 3),
            Text = "Lưu & Hồ sơ mới  (Ctrl+Enter)"
        };
        saveAndNewButton.Click += async (_, _) => await _desktopController.SaveAndNewDossier();

        _sessionStatusLabel = new WF.Label
        {
            AutoSize = false,
            Dock = WF.DockStyle.Bottom,
            Height = 30,
            Padding = new WF.Padding(14, 6, 8, 2),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };

        var productInfo = new WF.Label
        {
            AutoSize = false,
            Dock = WF.DockStyle.Bottom,
            Height = 68,
            Padding = new WF.Padding(14, 4, 8, 8),
            TextAlign = System.Drawing.ContentAlignment.BottomLeft,
            Text = $"{APP_NAME}\r\nPhiên bản: {APP_VERSION}\r\nTác giả: {APP_AUTHOR}"
        };

        sidebarPanel.Controls.Add(saveAndNewButton);
        sidebarPanel.Controls.Add(_sessionStatusLabel);
        sidebarPanel.Controls.Add(productInfo);

        _imageList.ImagesUpdated += (_, _) => UpdateSessionStatus();
        _imageList.ImagesThumbnailChanged += (_, _) => UpdateSessionStatus();
        _imageList.ImagesThumbnailInvalidated += (_, _) => UpdateSessionStatus();

        _statusTimer = new WF.Timer { Interval = 500 };
        _statusTimer.Tick += (_, _) => UpdateSessionStatus();
        _statusTimer.Start();
        UpdateSessionStatus();
    }

    private void UpdateSessionStatus()
    {
        if (_sessionStatusLabel == null || _sessionStatusLabel.IsDisposed)
        {
            return;
        }
        if (_sessionStatusLabel.InvokeRequired)
        {
            _sessionStatusLabel.BeginInvoke(new Action(UpdateSessionStatus));
            return;
        }

        int pages = _imageList.Images.Count;
        int blankWarnings = _imageList.Images.Count(x => x.IsBlankPageCandidate);
        int darkWarnings = _imageList.Images.Count(x => x.IsDarkPageCandidate);
        bool saved = pages > 0 && !_imageList.HasUnsavedChanges;

        if (string.Equals(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "vi",
                StringComparison.OrdinalIgnoreCase))
        {
            _sessionStatusLabel.Text = pages == 0
                ? "Sẵn sàng | F2: Quét nhanh"
                : $"{pages} trang | QC: {blankWarnings} vàng, {darkWarnings} đỏ | {(saved ? "Đã lưu" : "Chưa lưu")}";
        }
        else
        {
            _sessionStatusLabel.Text = pages == 0
                ? "Ready | F2: Quick Scan"
                : $"{pages} pages | QC: {blankWarnings} yellow, {darkWarnings} red | {(saved ? "Saved" : "Unsaved")}";
        }
    }

    protected override void UpdateTitle(ScanProfile? defaultProfile)
    {
        Title = defaultProfile == null || string.IsNullOrWhiteSpace(defaultProfile.DisplayName)
            ? APP_NAME
            : $"{APP_NAME} - {defaultProfile.DisplayName}";
    }
}
