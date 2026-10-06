namespace OpenSteps.Core.Models;

public sealed class AppSettings
{
    public ScreenshotMode ScreenshotMode { get; set; } = ScreenshotMode.FullDesktop;

    public bool SuggestTrimAfterRecording { get; set; } = true;

    /// <summary>OpenAI API key encrypted for the current Windows user (DPAPI), base64 encoded.</summary>
    public string? OpenAiApiKeyProtected { get; set; }

    public string? OpenAiModel { get; set; }
}
