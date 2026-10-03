using System.Globalization;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.Scan;
using NAPS2.Update;

namespace NAPS2.EtoForms.Ui;

public class AboutForm : EtoDialogBase
{
    private const string NAPS2_HOMEPAGE = "https://www.naps2.com";
    private const string AUTHOR_EMAIL = "checongphuoc@gmail.com";
    private const string APP_NAME = "CCP SCAN HỒ SƠ ĐẢNG VIÊN";
    private const string UNIT_NAME = "ĐẢNG ỦY PHƯỜNG TUY HÒA, TỈNH ĐẮK LẮK";

    private readonly CheckBox _enableDebugLogging = C.CheckBox(UiStrings.EnableDebugLogging);

    public AboutForm(Naps2Config config, UpdateChecker updateChecker, ScanningContext scanningContext)
        : base(config)
    {
        bool isVietnamese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi";
        Title = isVietnamese ? $"Giới thiệu - {APP_NAME}" : $"About - {APP_NAME}";
        IconName = "information_small";

        _enableDebugLogging.Checked = config.Get(c => c.EnableDebugLogging);
        _enableDebugLogging.CheckedChanged += (_, _) =>
        {
            config.User.Set(c => c.EnableDebugLogging, _enableDebugLogging.IsChecked());
            NLogConfig.EnvDebugLogging = _enableDebugLogging.IsChecked();
            scanningContext.WorkerFactory?.RecreateSpareWorkers();
        };
    }

    protected override void BuildLayout()
    {
        FormStateController.Resizable = false;
        FormStateController.RestoreFormState = false;
        LayoutController.DefaultSpacing = 4;

        bool isVietnamese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi";
        string authorLabel = isVietnamese ? "Tác giả phần mềm:" : "Software author:";
        string emailLabel = "Email:";
        string platformLabel = isVietnamese ? "Nền tảng mã nguồn mở:" : "Open-source platform:";
        string licenseText = isVietnamese
            ? "Dựa trên NAPS2. Giấy phép nguồn mở của NAPS2 và các thành phần liên quan được giữ nguyên theo dự án gốc."
            : "Based on NAPS2. The original open-source licenses for NAPS2 and related components remain in effect.";

        LayoutController.Content = L.Row(
            L.Column(new ImageView { Image = Icons.scanner_128.ToEtoImage() }).Padding(right: 8),
            L.Column(
                C.NoWrap(APP_NAME),
                C.NoWrap(UNIT_NAME),
                C.TextSpace(),
                C.NoWrap(string.Format(MiscResources.Version, AssemblyHelper.Version)),
                C.NoWrap($"{authorLabel} Chế Công Phước"),
                C.NoWrap($"{emailLabel} {AUTHOR_EMAIL}"),
                C.TextSpace(),
                C.NoWrap($"{platformLabel} NAPS2 - Not Another PDF Scanner"),
                C.UrlLink(NAPS2_HOMEPAGE),
                C.NoWrap(licenseText),
                Config.AppLocked.Has(c => c.EnableDebugLogging)
                    ? C.None()
                    : new[] { C.Spacer(), _enableDebugLogging.Padding(left: 4) }.Expand(),
                C.TextSpace(),
                L.Row(
                    C.Filler(),
                    C.DialogButton(this, UiStrings.OK, true, true)
                )
            )
        );
    }
}
