namespace SalesApp.Core.Entities;

/// <summary>
/// One of a person's own settings, kept on the server so it follows them to any computer: light/dark, layout, saved
/// list views, favourites, recently viewed records, table columns. The value is JSON the web app owns; the server
/// only checks its size.
/// </summary>
public sealed class UserSetting
{
    public Guid UserId { get; set; }

    /// <summary>The setting's name, such as "prefs" or "views.invoices".</summary>
    public required string Key { get; set; }

    public required string ValueJson { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
