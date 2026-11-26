namespace BusPing.Models;

public enum AppThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>User settings, persisted in MAUI Preferences.</summary>
public sealed class AppSettings
{
    public AppThemeMode Theme { get; set; } = AppThemeMode.System;

    /// <summary>Seconds between live-data polls while a watch window is active.</summary>
    public int PollSeconds { get; set; } = 5;

    public int DefaultLeadMinutes { get; set; } = 5;

    public bool SoundEnabled { get; set; } = true;

    public bool ConfirmBeforeDelete { get; set; } = true;

    public string Language { get; set; } = "en";

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
