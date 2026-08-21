using NAPS2.EtoForms.Desktop;
using NAPS2.EtoForms.Notifications;
using NAPS2.Scan;

namespace NAPS2.EtoForms.Ui;

/// <summary>
/// Windows desktop shell customized for the CCP Party-member dossier scanning workflow.
/// </summary>
public class CcpWinFormsDesktopForm : WinFormsDesktopForm
{
    private const string APP_NAME = "CCP SCAN HỒ SƠ ĐẢNG VIÊN";

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

    protected override void UpdateTitle(ScanProfile? defaultProfile)
    {
        Title = defaultProfile == null || string.IsNullOrWhiteSpace(defaultProfile.DisplayName)
            ? APP_NAME
            : $"{APP_NAME} - {defaultProfile.DisplayName}";
    }
}
