using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Applies the app theme.
/// </summary>
public interface IThemeService
{
    AppTheme GetCurrentTheme();

    void ApplyTheme(AppTheme theme);
}

/// <summary>
/// Bridges theme changes to the Avalonia application.
/// </summary>
public sealed class AvaloniaThemeService : IThemeService
{
    public AppTheme GetCurrentTheme()
    {
        ThemeVariant? currentVariant = Application.Current?.ActualThemeVariant;
        if (currentVariant == ThemeVariant.Dark)
            return AppTheme.Dark;

        return currentVariant == AppThemeVariants.Sepia
            ? AppTheme.Sepia
            : AppTheme.Light;
    }

    public void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is not Application application)
            return;

        EnsureThemePalettesRegistered(application.Resources);
        application.RequestedThemeVariant = theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Sepia => AppThemeVariants.Sepia,
            _ => ThemeVariant.Light
        };
    }

    private static void EnsureThemePalettesRegistered(IResourceDictionary resources)
    {
        if (resources.ThemeDictionaries.ContainsKey(AppThemeVariants.Sepia))
            return;

        resources.ThemeDictionaries[AppThemeVariants.Sepia] = BuildSepiaPalette();
    }

    private static ResourceDictionary BuildSepiaPalette()
    {
        ResourceDictionary dictionary = new();

        dictionary["SystemAccentColor"] = Color.Parse("#FFB16A2D");
        dictionary["SystemAccentColorDark1"] = Color.Parse("#FF9A5C24");
        dictionary["SystemAccentColorDark2"] = Color.Parse("#FF7E4A1D");
        dictionary["SystemAccentColorDark3"] = Color.Parse("#FF653A16");
        dictionary["SystemAccentColorLight1"] = Color.Parse("#FFBC7740");
        dictionary["SystemAccentColorLight2"] = Color.Parse("#FFC98A58");
        dictionary["SystemAccentColorLight3"] = Color.Parse("#FFD8A879");

        dictionary["PmWindowBackground"] = Brush("#FFF0E6D6");
        dictionary["PmCardBackground"] = Brush("#FFF8F1E6");
        dictionary["PmCardBorderBrush"] = Brush("#FFD1C2A8");
        dictionary["PmHeroBackground"] = Gradient("#FFF0E2CA", "#FFE2D0AF");
        dictionary["PmHeroBorderBrush"] = Brush("#FFCCB48E");
        dictionary["PmHeadingForeground"] = Brush("#FF2E241B");
        dictionary["PmSectionTitleForeground"] = Brush("#FF48392B");
        dictionary["PmMutedForeground"] = Brush("#FF756553");
        dictionary["PmNavSurfaceBackground"] = Brush("#FFF5EDE0");
        dictionary["PmNavSurfaceBorderBrush"] = Brush("#FFD3C4AA");
        dictionary["PmNavItemBackground"] = Brush("#00000000");
        dictionary["PmNavItemPointerOver"] = Brush("#FFE9DCC5");
        dictionary["PmNavItemSelectedBackground"] = Brush("#FFE1CFB0");
        dictionary["PmNavItemSelectedBorderBrush"] = Brush("#FFC9AF82");
        dictionary["PmNavItemForeground"] = Brush("#FF48392B");
        dictionary["PmPageBannerBackground"] = Brush("#FFF4EBDE");
        dictionary["PmPageBannerBorderBrush"] = Brush("#FFD4C4AB");
        dictionary["PmHelpBadgeBackground"] = Brush("#FFE8D9C0");
        dictionary["PmHelpBadgeBorderBrush"] = Brush("#FFCDB18A");
        dictionary["PmUpdateDotBackground"] = Brush("#FF4A9A5D");
        dictionary["PmUpdatePanelBackground"] = Brush("#FFF6E8C7");
        dictionary["PmUpdatePanelBorderBrush"] = Brush("#FFD5B16D");
        dictionary["PmQueueFeedbackBackground"] = Gradient("#FFBF7A36", "#FF9E6024");
        dictionary["PmQueueFeedbackBorderBrush"] = Brush("#FFD39A64");
        dictionary["PmQueueFeedbackForeground"] = Brush("#FFFFFBF6");
        dictionary["PmQueueFeedbackBadgeBackground"] = Brush("#26FFFFFF");
        dictionary["PmQueueFeedbackBadgeBorderBrush"] = Brush("#3BFFFFFF");

        dictionary["PmPrimaryBackground"] = Brush("#FFB16A2D");
        dictionary["PmPrimaryPointerOver"] = Brush("#FFBC7740");
        dictionary["PmPrimaryPressed"] = Brush("#FF9A5C24");
        dictionary["PmPrimaryBorder"] = Brush("#FFC88A52");
        dictionary["PmPrimaryDisabledBackground"] = Brush("#FFDED1BE");
        dictionary["PmPrimaryDisabledBorder"] = Brush("#FFCCBBA5");
        dictionary["PmPrimaryDisabledForeground"] = Brush("#FF8B7A68");
        dictionary["PmTrimStartBackground"] = Brush("#FF6D9B62");
        dictionary["PmTrimRangeBackground"] = Brush("#FFC8D9A9");
        dictionary["PmCancelBackground"] = Brush("#FFBD6B56");
        dictionary["PmCancelPointerOver"] = Brush("#FFC97761");
        dictionary["PmCancelPressed"] = Brush("#FFA95C49");
        dictionary["PmCancelBorder"] = Brush("#FFD28A78");

        dictionary["PmDropzoneBackground"] = Brush("#FFF4ECE0");
        dictionary["PmDropzoneBorderBrush"] = Brush("#FFC8B799");
        dictionary["PmDropzoneDragBackground"] = Brush("#FFE7D9C1");
        dictionary["PmDropzoneDragBorderBrush"] = Brush("#FFB27B3F");

        dictionary["PmStatBackground"] = Brush("#FFF2EBDD");
        dictionary["PmStatBorderBrush"] = Brush("#FFD4C3A7");
        dictionary["PmInsetBackground"] = Brush("#FFF0E7D8");
        dictionary["PmInsetBorderBrush"] = Brush("#FFD3C1A4");
        dictionary["PmFieldBackground"] = Brush("#FFF8F1E6");
        dictionary["PmFieldBorderBrush"] = Brush("#FFCBB89A");
        dictionary["PmFieldPointerOverBorderBrush"] = Brush("#FFC1894C");
        dictionary["PmSliderThumbBackground"] = Brush("#FFB16A2D");
        dictionary["PmSliderThumbPointerOverBackground"] = Brush("#FFBC7740");
        dictionary["PmSliderThumbBorderBrush"] = Brush("#FFF8F1E6");
        dictionary["PmSliderFillBackground"] = Brush("#FFB16A2D");
        dictionary["PmSliderFillPointerOverBackground"] = Brush("#FFB16A2D");
        dictionary["PmSliderTrackBackground"] = Brush("#FF8C8376");
        dictionary["PmSliderDisabledThumbBackground"] = Brush("#FFC5B69E");
        dictionary["PmSliderDisabledTrackBackground"] = Brush("#FFD0C1A8");
        dictionary["PmStatusIdleForeground"] = Brush("#FF56473A");
        dictionary["PmStatusAnalysingForeground"] = Brush("#FF7C90B6");
        dictionary["PmStatusEncodingForeground"] = Brush("#FF4F855E");
        dictionary["PmStatusCancelledForeground"] = Brush("#FF9B6D2C");
        dictionary["PmStatusErrorForeground"] = Brush("#FFAD5F4B");
        dictionary["PmStatusDoneForeground"] = Brush("#FF4F855E");
        dictionary["PmVideoSurfaceBackground"] = Brush("#FF17120D");
        dictionary["PmVideoSurfaceBorderBrush"] = Brush("#FF4E4031");
        dictionary["PmVideoEmptyBackground"] = Brush("#FFDDD0BC");
        dictionary["PmErrorForeground"] = Brush("#FFB65441");

        return dictionary;
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static LinearGradientBrush Gradient(string startColor, string endColor) =>
        new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse(startColor), 0),
                new GradientStop(Color.Parse(endColor), 1)
            ]
        };
}
