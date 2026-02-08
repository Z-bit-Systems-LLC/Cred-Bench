using System.Globalization;

namespace CredBench.Core.Services;

/// <summary>
/// Default implementation of the localization service using Core resource files.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private CultureInfo _currentCulture;

    /// <summary>
    /// Initializes a new instance of the LocalizationService.
    /// </summary>
    public LocalizationService()
    {
        _currentCulture = CultureInfo.CurrentUICulture;

        SupportedCultures = new List<CultureInfo>
        {
            new("en-US")
        }.AsReadOnly();
    }

    /// <inheritdoc />
    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture.Equals(value)) return;
            ChangeCulture(value);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CultureInfo> SupportedCultures { get; }

    /// <inheritdoc />
    public event EventHandler<CultureInfo>? CultureChanged;

    /// <inheritdoc />
    public string GetString(string key)
    {
        return Resources.Resources.GetString(key);
    }

    /// <inheritdoc />
    public string GetString(string key, params object[] args)
    {
        try
        {
            var format = GetString(key);
            return string.Format(_currentCulture, format, args);
        }
        catch
        {
            return $"[{key}]";
        }
    }

    /// <inheritdoc />
    public void ChangeCulture(CultureInfo culture)
    {
        if (_currentCulture.Equals(culture)) return;

        _currentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        Resources.Resources.ChangeCulture(culture);
        CultureChanged?.Invoke(this, culture);
    }

    /// <inheritdoc />
    public void ChangeCulture(string cultureName)
    {
        try
        {
            ChangeCulture(new CultureInfo(cultureName));
        }
        catch (CultureNotFoundException)
        {
            ChangeCulture(new CultureInfo("en-US"));
        }
    }
}
