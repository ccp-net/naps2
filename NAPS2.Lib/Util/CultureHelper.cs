using System.Collections;
using System.Globalization;
using NAPS2.Lang;

namespace NAPS2.Util;

/// <summary>
/// A helper to for culture-related functionality.
/// </summary>
public class CultureHelper
{
    private static readonly HashSet<string> CCP_SUPPORTED_LANGUAGES = new(StringComparer.OrdinalIgnoreCase)
    {
        "vi",
        "en"
    };

    private readonly Naps2Config _config;

    public CultureHelper(Naps2Config config)
    {
        _config = config;
    }

    /// <summary>
    /// Sets thread and resource cultures based on the culture in the NAPS2 config (if present).
    /// </summary>
    public void SetCulturesFromConfig()
    {
        var cultureId = _config.Get(c => c.Culture);
        if (!string.IsNullOrWhiteSpace(cultureId))
        {
            try
            {
                var culture = new CultureInfo(cultureId);

                // Apply the selected language to the current UI thread immediately as well as future threads.
                // The language command recreates the desktop form on the existing UI thread, so setting only
                // DefaultThreadCurrentUICulture would leave the recreated form using the old language.
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;

                MiscResources.Culture = culture;
                SettingsResources.Culture = culture;
                LanguageNames.Culture = culture;
            }
            catch (CultureNotFoundException e)
            {
                Log.ErrorException("Invalid culture.", e);
            }
        }
    }

    public IEnumerable<(string langCode, string langName)> GetAllCultures()
    {
        // Read a list of languages from the Languages.resx file
        var resourceManager = LanguageNames.ResourceManager;
        var resourceSet = resourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
        foreach (DictionaryEntry entry in resourceSet.Cast<DictionaryEntry>().OrderBy(x => x.Value))
        {
            var langCode = ((string) entry.Key).Replace("_", "-");
            var langName = (string) entry.Value!;
            yield return (langCode, langName);
        }
    }

    public IEnumerable<(string langCode, string langName)> GetAvailableCultures()
    {
        // CCP Scan is intentionally limited to Vietnamese and English to keep the interface simple for the target unit.
        return GetAllCultures()
            .Where(x => CCP_SUPPORTED_LANGUAGES.Contains(x.langCode))
            .OrderBy(x => x.langCode == "vi" ? 0 : 1);
    }
}
