namespace StreamOrchestrator.Windows;

/// <summary>
/// Heuristic for "is this window likely a presentation/document the user would project":
/// slide apps, PDF viewers, image viewers, and browsers (web slides / Google Slides / Canva).
/// </summary>
public static class PresentationFilter
{
    // Process names (lower-case, without ".exe").
    private static readonly HashSet<string> PresentationApps = new(StringComparer.OrdinalIgnoreCase)
    {
        // Slide / office
        "powerpnt",      // Microsoft PowerPoint
        "wpp",           // WPS Presentation
        "et", "wps",     // WPS suite
        "soffice", "soffice.bin", // LibreOffice / OpenOffice (Impress)
        "keynote",
        // PDF viewers
        "acrobat", "acrord32", "acrocef",
        "sumatrapdf",
        "foxitreader", "foxitpdfreader", "foxit reader",
        "pdfxcview", "nitropdf", "drawboard pdf", "msixpdf",
        // Image / media viewers (slides exported as images, or photo walls)
        "photos", "microsoft.photos", "photosapp",
        "quicklook",
        // Browsers (web-based slides: Google Slides, Canva, Prezi, Figma slides…)
        "msedge", "chrome", "firefox", "brave", "opera", "vivaldi", "iexplore",
        "zen", "arc", "librewolf", "waterfox", "floorp", "chromium", "thorium", "tor browser",
    };

    public static bool IsPresentation(string processName) =>
        !string.IsNullOrEmpty(processName) && PresentationApps.Contains(processName);
}
