using Eto.Forms;
using Eto.WinForms;
using NAPS2.EtoForms.Desktop;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Notifications;
using NAPS2.EtoForms.Widgets;
using NAPS2.EtoForms.WinForms;
using NAPS2.ImportExport.Images;
using NAPS2.PartyDossier;
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
    private const string APP_VERSION = "v0.2 Preview";
    private const string APP_AUTHOR = "Chế Công Phước";

    private readonly UiImageList _imageList;

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

        var bottomPanel = new WF.Panel
        {
            Dock = WF.DockStyle.Bottom,
            Height = 156,
            Padding = new WF.Padding(12, 4, 8, 4)
        };

        var dossierPanel = new WF.Panel
        {
            Dock = WF.DockStyle.Top,
            Height = 82
        };

        var dossierTitle = new WF.Label
        {
            AutoSize = true,
            Left = 0,
            Top = 2,
            Text = "Loại hồ sơ (01-104):"
        };

        var dossierIdTextBox = new WF.TextBox
        {
            Left = 0,
            Top = 24,
            Width = 62,
            MaxLength = 3
        };

        var assignButton = new WF.Button
        {
            Left = 68,
            Top = 22,
            Width = 58,
            Height = 26,
            Text = "Gán"
        };

        var clearButton = new WF.Button
        {
            Left = 132,
            Top = 22,
            Width = 58,
            Height = 26,
            Text = "Bỏ gán"
        };

        var dossierStatus = new WF.Label
        {
            AutoEllipsis = true,
            Left = 0,
            Top = 52,
            Width = 235,
            Height = 26,
            Text = "Chọn thumbnail rồi nhập STT hồ sơ."
        };

        void AssignDocumentType()
        {
            if (!_imageList.Selection.Any())
            {
                dossierStatus.Text = "Chưa chọn thumbnail.";
                return;
            }

            if (!int.TryParse(dossierIdTextBox.Text.Trim(), out var documentTypeId) ||
                PartyDossierDocumentCatalog.Get(documentTypeId) is not { } documentType)
            {
                dossierStatus.Text = "STT phải từ 01 đến 104.";
                dossierIdTextBox.SelectAll();
                dossierIdTextBox.Focus();
                return;
            }

            foreach (var image in _imageList.Selection.ToList())
            {
                image.SetPartyDossierDocumentType(documentTypeId);
            }

            dossierIdTextBox.Text = documentTypeId.ToString("00");
            dossierStatus.Text = $"Đã gán {_imageList.Selection.Count}: {documentType.DisplayName}";
            dossierIdTextBox.SelectAll();
            dossierIdTextBox.Focus();
        }

        assignButton.Click += (_, _) => AssignDocumentType();
        dossierIdTextBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == WF.Keys.Enter)
            {
                AssignDocumentType();
                e.SuppressKeyPress = true;
            }
        };
        clearButton.Click += (_, _) =>
        {
            if (!_imageList.Selection.Any())
            {
                dossierStatus.Text = "Chưa chọn thumbnail.";
                return;
            }
            foreach (var image in _imageList.Selection.ToList())
            {
                image.SetPartyDossierDocumentType(null);
            }
            dossierStatus.Text = $"Đã bỏ gán {_imageList.Selection.Count} trang.";
        };

        dossierPanel.Controls.Add(dossierTitle);
        dossierPanel.Controls.Add(dossierIdTextBox);
        dossierPanel.Controls.Add(assignButton);
        dossierPanel.Controls.Add(clearButton);
        dossierPanel.Controls.Add(dossierStatus);

        var productInfo = new WF.Label
        {
            AutoSize = false,
            Dock = WF.DockStyle.Bottom,
            Height = 68,
            Padding = new WF.Padding(0, 4, 0, 4),
            TextAlign = System.Drawing.ContentAlignment.BottomLeft,
            Text = $"{APP_NAME}\r\nPhiên bản: {APP_VERSION}\r\nTác giả: {APP_AUTHOR}"
        };

        bottomPanel.Controls.Add(productInfo);
        bottomPanel.Controls.Add(dossierPanel);
        sidebarPanel.Controls.Add(bottomPanel);
        bottomPanel.BringToFront();
    }

    protected override void UpdateTitle(ScanProfile? defaultProfile)
    {
        Title = defaultProfile == null || string.IsNullOrWhiteSpace(defaultProfile.DisplayName)
            ? APP_NAME
            : $"{APP_NAME} - {defaultProfile.DisplayName}";
    }
}
