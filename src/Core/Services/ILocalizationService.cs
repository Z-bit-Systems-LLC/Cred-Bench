using System.Globalization;

namespace CredBench.Core.Services;

/// <summary>
/// Service for managing localization and culture-specific formatting
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets or sets the current culture
    /// </summary>
    CultureInfo CurrentCulture { get; set; }

    /// <summary>
    /// Gets the list of supported cultures
    /// </summary>
    IReadOnlyList<CultureInfo> SupportedCultures { get; }

    /// <summary>
    /// Event raised when the current culture changes
    /// </summary>
    event EventHandler<CultureInfo>? CultureChanged;

    /// <summary>
    /// Gets a localized string by key
    /// </summary>
    string GetString(string key);

    /// <summary>
    /// Gets a localized string by key with format arguments
    /// </summary>
    string GetString(string key, params object[] args);

    /// <summary>
    /// Changes the current culture and notifies all components
    /// </summary>
    void ChangeCulture(CultureInfo culture);

    /// <summary>
    /// Changes the current culture by culture name
    /// </summary>
    void ChangeCulture(string cultureName);
}
