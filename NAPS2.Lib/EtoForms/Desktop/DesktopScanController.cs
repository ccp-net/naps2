using NAPS2.EtoForms.Ui;
using NAPS2.Scan;
#if !MAC
using NAPS2.Wia;
#endif

namespace NAPS2.EtoForms.Desktop;

public class DesktopScanController : IDesktopScanController
{
    private readonly Naps2Config _config;
    private readonly IProfileManager _profileManager;
    private readonly IFormFactory _formFactory;
    private readonly IScanPerformer _scanPerformer;
    private readonly DesktopImagesController _desktopImagesController;
    private readonly IDesktopSubFormController _desktopSubFormController;
    private readonly DesktopFormProvider _desktopFormProvider;
    private readonly ThumbnailController _thumbnailController;

    public DesktopScanController(Naps2Config config, IProfileManager profileManager, IFormFactory formFactory,
        IScanPerformer scanPerformer, DesktopImagesController desktopImagesController,
        IDesktopSubFormController desktopSubFormController, DesktopFormProvider desktopFormProvider,
        ThumbnailController thumbnailController)
    {
        _config = config;
        _profileManager = profileManager;
        _formFactory = formFactory;
        _scanPerformer = scanPerformer;
        _desktopImagesController = desktopImagesController;
        _desktopSubFormController = desktopSubFormController;
        _desktopFormProvider = desktopFormProvider;
        _thumbnailController = thumbnailController;
    }

    private ScanParams DefaultScanParams() =>
        new()
        {
            // CCP Scan is review-first: scanned pages always return to the thumbnail workspace for inspection/editing.
            // Saving is a separate explicit action (Save PDF / Ctrl+S / Save & New Dossier).
            NoAutoSave = true,
            OcrParams = _config.OcrAfterScanningParams(),
            ThumbnailSize = _thumbnailController.RenderSize
        };

    public async Task ScanWithDevice(string deviceID)
    {
        _desktopFormProvider.DesktopForm.BringToFront();
        ScanProfile? profile;
        if (_profileManager.DefaultProfile?.Device?.ID == deviceID)
        {
            profile = _profileManager.DefaultProfile;
        }
        else
        {
            profile = _profileManager.Profiles.FirstOrDefault(x => x.Device != null && x.Device.ID == deviceID);
        }
        if (profile == null)
        {
            if (_config.Get(c => c.NoUserProfiles) && _profileManager.Profiles.Any(x => x.IsLocked))
            {
                return;
            }

            var editSettingsForm = _formFactory.Create<EditProfileForm>();
            editSettingsForm.NewProfile = true;
            editSettingsForm.ScanProfile = _config.DefaultProfileSettings();
#if !MAC
#if NET6_0_OR_GREATER
            if (OperatingSystem.IsWindows())
            {
#endif
                try
                {
                    using var deviceManager = new WiaDeviceManager();
                    using var device = deviceManager.FindDevice(deviceID);
                    editSettingsForm.SetDevice(new ScanDevice(Driver.Wia, deviceID, device.Name()));
                }
                catch (WiaException)
                {
                }
#if NET6_0_OR_GREATER
            }
#endif
#endif
            editSettingsForm.ShowModal();
            if (!editSettingsForm.Result)
            {
                return;
            }
            profile = editSettingsForm.ScanProfile;
            _profileManager.Mutate(new ListMutation<ScanProfile>.Append(profile),
                ListSelection.Empty<ScanProfile>());
            MaybeSetDefaultProfile(profile);
        }

        await DoScan(profile);
    }

    public async Task ScanQuick()
    {
        if (_profileManager.DefaultProfile != null)
        {
            await DoScan(_profileManager.DefaultProfile);
            return;
        }

        if (_profileManager.Profiles.Count == 0)
        {
            await ScanWithNewProfile();
        }
        else
        {
            _profileManager.DefaultProfile = _profileManager.Profiles[0];
            await DoScan(_profileManager.DefaultProfile);
        }
    }

    public async Task ScanDefault()
    {
        // Main Scan button: scan immediately using the saved profile exactly as configured, then stop at the thumbnail
        // workspace for review/editing. CCP defaults belong to profile creation, never to the Scan button itself.
        await ScanQuick();
    }

    public async Task ScanWithNewProfile()
    {
        var editSettingsForm = _formFactory.Create<EditProfileForm>();
        editSettingsForm.NewProfile = true;
        editSettingsForm.ScanProfile = _config.DefaultProfileSettings();
        editSettingsForm.ShowModal();
        if (!editSettingsForm.Result)
        {
            return;
        }
        _profileManager.Mutate(new ListMutation<ScanProfile>.Append(editSettingsForm.ScanProfile),
            ListSelection.Empty<ScanProfile>());
        MaybeSetDefaultProfile(editSettingsForm.ScanProfile);

        await DoScan(editSettingsForm.ScanProfile);
    }

    public async Task ScanWithProfile(ScanProfile profile)
    {
        MaybeSetDefaultProfile(profile);
        await DoScan(profile);
    }

    private void MaybeSetDefaultProfile(ScanProfile profile)
    {
        if (_config.Get(c => c.ScanChangesDefaultProfile) || _profileManager.DefaultProfile == null)
        {
            _profileManager.DefaultProfile = profile;
        }
    }

    private async Task DoScan(ScanProfile profile)
    {
        // IMPORTANT: Do not rewrite scan settings here. The profile is the single source of truth.
        // This preserves Advanced settings such as Stretch/Crop to page size, blank-page handling, TWAIN mode,
        // automatic page-size detection, deskew and the operator-selected DPI/page size/color mode across scans.
        var images =
            _scanPerformer.PerformScan(profile, DefaultScanParams(), _desktopFormProvider.DesktopForm.NativeHandle);
        var imageCallback = _desktopImagesController.ReceiveScannedImage();
        await foreach (var image in images)
        {
            imageCallback(image);
        }
        _desktopFormProvider.DesktopForm.BringToFront();
    }
}
