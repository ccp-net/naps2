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
    private const string APP_VERSION = "v0.2.4 Fast Scan Preview";
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

        // Keep the bottom workflow controls compact so they never overlap the main Scan button on 768p displays.
        var saveAndNewButton = new WF.Button
        {
            AutoSize = false,
            Dock = WF.DockStyle.Bottom,
            Height = 30,
            Margin = new WF.Padding(12, 2, 12, 2),
            Text = "Lưu & Hồ sơ mới  (Ctrl+Enter)"
        };
        saveAndNewButton.Click += async (_, _) => await _desktopController.SaveAndNewDossier();

        _sessionStatusLabel = new WF.Label
        {
            AutoSize = false,
            Dock = WF.DockStyle.Bottom,
            Height = 26,
            Padding = new WF.Padding(14, 4, 8, 1),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = System.Drawing.Color.RoyalBlue
        };

        var baseFont = System.Drawing.SystemFonts.MessageBoxFont;
        var productInfo = new WF.Label
        {
            AutoSize = false,
            Dock = WF.DockStyle.Bottom,
            Height = 46,
            Padding = new WF.Padding(14, 2, 8, 5),
            TextAlign = System.Drawing.ContentAlignment.BottomLeft,
            Font = new System.Drawing.Font(baseFont.FontFamily, baseFont.Size * 0.70f, System.Drawing.FontStyle.Regular),
            Text = $"{APP_NAME}\r\nPhiên bản: {APP_VERSION}\r\nTác giả: {APP_AUTHOR}"
        };

        sidebarPanel.Controls.Add(saveAndNewButton);
        sidebarPanel.Controls.Add(_sessionStatusLabel);
        sidebarPanel.Controls.Add(productInfo);

        // Make the primary scan action visually dominant and move it slightly upward to preserve spacing from the
        // bottom workflow controls. Search recursively because the Eto WinForms backend nests the native button.
        CustomizePrimaryScanButton(sidebarPanel);

        _imageList.ImagesUpdated += (_, _) => UpdateSessionStatus();
        _imageList.ImagesThumbnailChanged += (_, _) => UpdateSessionStatus();
        _imageList.ImagesThumbnailInvalidated += (_, _) => UpdateSessionStatus();

        _statusTimer = new WF.Timer { Interval = 500 };
        _statusTimer.Tick += (_, _) => UpdateSessionStatus();
        _statusTimer.Start();
        UpdateSessionStatus();
    }

    private static void CustomizePrimaryScanButton(WF.Control root)
    {
        foreach (WF.Control control in root.Controls)
        {
            if (control is WF.Button button &&
                (string.Equals(button.Text.Trim(), "Quét", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(button.Text.Trim(), "Scan", StringComparison.OrdinalIgnoreCase)))
            {
                button.AutoSize = false;
                button.Width = Math.Max(button.Width, 150);
                button.Height = Math.Max(button.Height, 42);
                button.Top = Math.Max(0, button.Top - 10);
                if (button.Parent != null)
                {
                    button.Left = Math.Max(0, (button.Parent.ClientSize.Width - button.Width) / 2);
                }
                button.Font = new System.Drawing.Font(button.Font, System.Drawing.FontStyle.Bold);
                button.BackColor = System.Drawing.Color.FromArgb(0, 120, 215);
                button.ForeColor = System.Drawing.Color.White;
                button.UseVisualStyleBackColor = false;
                return;
            }

            if (control.HasChildren)
            {
                CustomizePrimaryScanButton(control);
            }
        }
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
                : $"{pages:00} trang | QC: {blankWarnings} vàng, {darkWarnings} đỏ | {(saved ? "Đã lưu" : "Chưa lưu")}";
        }
        else
        {
            _sessionStatusLabel.Text = pages == 0
                ? "Ready | F2: Quick Scan"
                : $"{pages:00} pages | QC: {blankWarnings} yellow, {darkWarnings} red | {(saved ? "Saved" : "Unsaved")}";
        }
    }

    protected override void UpdateTitle(ScanProfile? defaultProfile)
    {
        Title = defaultProfile == null || string.IsNullOrWhiteSpace(defaultProfile.DisplayName)
            ? APP_NAME
            : $"{APP_NAME} - {defaultProfile.DisplayName}";
    }
}
