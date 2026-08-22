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
    private const string APP_VERSION = "v0.1.1 Preview";
    private const string APP_AUTHOR = "Chế Công Phước";

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
    }

    /// <summary>
    /// Replace the original Rotate drop-down exactly where it is normally created. Rotate Left and Rotate Right use
    /// the same stacked control as Move Up/Move Down, while Flip is a direct one-click button next to them.
    /// </summary>
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

        // Keep product identification visible without taking space from the scanning controls. The label is docked to
        // the bottom of the existing left sidebar so it remains there when the main window is resized.
        var splitter = ((LayoutLeftPanel) LayoutController.Content!).Splitter;
        var sidebarPanel = (WF.Panel) splitter.Panel1.ToNative();
        var productInfo = new WF.Label
        {
            AutoSize = false,
            Dock = WF.DockStyle.Bottom,
            Height = 68,
            Padding = new WF.Padding(14, 4, 8, 8),
            TextAlign = System.Drawing.ContentAlignment.BottomLeft,
            Text = $"{APP_NAME}\r\nPhiên bản: {APP_VERSION}\r\nTác giả: {APP_AUTHOR}"
        };
        sidebarPanel.Controls.Add(productInfo);
        productInfo.BringToFront();
    }

    protected override void UpdateTitle(ScanProfile? defaultProfile)
    {
        Title = defaultProfile == null || string.IsNullOrWhiteSpace(defaultProfile.DisplayName)
            ? APP_NAME
            : $"{APP_NAME} - {defaultProfile.DisplayName}";
    }
}
