using System.ComponentModel;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.App;

public sealed class TrimRow : INotifyPropertyChanged
{
    private bool _keep = true;
    private string? _reason;

    public TrimRow(RecordedStep step, int number)
    {
        Step = step;
        Number = number;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RecordedStep Step { get; }

    public int Number { get; }

    public string Title => Step.DisplayTitle;

    public bool Keep
    {
        get => _keep;
        set
        {
            if (_keep == value)
            {
                return;
            }

            _keep = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Keep)));
        }
    }

    public string? Reason
    {
        get => _reason;
        set
        {
            _reason = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Reason)));
        }
    }
}

public partial class TrimStepsWindow : Window
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(2) };

    private readonly List<TrimRow> _rows;
    private readonly string _guideTitle;
    private readonly AppSettings _settings;
    private readonly Func<Task> _saveSettings;

    public TrimStepsWindow(
        IReadOnlyList<RecordedStep> steps,
        string guideTitle,
        AppSettings settings,
        Func<Task> saveSettings,
        IReadOnlyList<StepTrimSuggestion>? initialSuggestions = null)
    {
        InitializeComponent();
        _guideTitle = guideTitle;
        _settings = settings;
        _saveSettings = saveSettings;
        _rows = steps.Select((step, i) => new TrimRow(step, i + 1)).ToList();
        foreach (var row in _rows)
        {
            row.PropertyChanged += (_, _) => UpdateApplyButton();
        }

        RowsList.ItemsSource = _rows;
        ModelBox.Text = string.IsNullOrWhiteSpace(settings.OpenAiModel) ? OpenAiStepTrimmer.DefaultModel : settings.OpenAiModel;
        SuggestAfterRecordingBox.IsChecked = settings.SuggestTrimAfterRecording;
        UpdateApiKeyHint();
        if (initialSuggestions is not null)
        {
            ApplySuggestions(initialSuggestions, "rules");
        }

        UpdateApplyButton();
    }

    public IReadOnlyList<RecordedStep> StepsToRemove => _rows.Where(row => !row.Keep).Select(row => row.Step).ToList();

    public IReadOnlyList<RecordedStep> StepsToKeep => _rows.Where(row => row.Keep).Select(row => row.Step).ToList();

    private void SuggestRules_Click(object sender, RoutedEventArgs e)
    {
        ApplySuggestions(StepTrimmer.SuggestRemovals(_rows.Select(row => row.Step).ToList()), "rules");
    }

    private async void SuggestAi_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText.Text = "Add an OpenAI API key under OpenAI settings first.";
            return;
        }

        SuggestAiButton.IsEnabled = false;
        StatusText.Text = "Asking OpenAI...";
        try
        {
            var trimmer = new OpenAiStepTrimmer(HttpClient);
            var suggestions = await trimmer.SuggestRemovalsAsync(
                _guideTitle,
                _rows.Select(row => row.Step).ToList(),
                apiKey,
                ModelBox.Text);
            ApplySuggestions(suggestions, "AI");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"AI suggestion failed: {ex.Message}";
        }
        finally
        {
            SuggestAiButton.IsEnabled = true;
        }
    }

    private void KeepAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.Keep = true;
            row.Reason = null;
        }

        StatusText.Text = string.Empty;
    }

    private async void SaveAiSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            _settings.OpenAiApiKeyProtected = Protect(ApiKeyBox.Password.Trim());
            ApiKeyBox.Clear();
        }

        _settings.OpenAiModel = string.IsNullOrWhiteSpace(ModelBox.Text) ? null : ModelBox.Text.Trim();
        await _saveSettings();
        UpdateApiKeyHint();
        StatusText.Text = "OpenAI settings saved.";
    }

    private async void ClearKey_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Clear();
        _settings.OpenAiApiKeyProtected = null;
        await _saveSettings();
        UpdateApiKeyHint();
        StatusText.Text = "Saved API key removed.";
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (SuggestAfterRecordingBox.IsChecked != _settings.SuggestTrimAfterRecording)
        {
            _settings.SuggestTrimAfterRecording = SuggestAfterRecordingBox.IsChecked == true;
            await _saveSettings();
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ApplySuggestions(IReadOnlyList<StepTrimSuggestion> suggestions, string source)
    {
        var byId = suggestions.ToDictionary(suggestion => suggestion.StepId, suggestion => suggestion.Reason);
        foreach (var row in _rows)
        {
            var suggested = byId.TryGetValue(row.Step.Id, out var reason);
            row.Keep = !suggested;
            row.Reason = suggested ? reason : null;
        }

        StatusText.Text = suggestions.Count == 0
            ? $"No removals suggested ({source})."
            : $"{suggestions.Count} step{(suggestions.Count == 1 ? string.Empty : "s")} suggested for removal ({source}).";
    }

    private void UpdateApplyButton()
    {
        var count = _rows.Count(row => !row.Keep);
        ApplyButton.Content = count == 0 ? "Keep all steps" : $"Remove {count} step{(count == 1 ? string.Empty : "s")}";
    }

    private void UpdateApiKeyHint()
    {
        ApiKeyHint.Text = !string.IsNullOrWhiteSpace(_settings.OpenAiApiKeyProtected)
            ? "A key is saved (encrypted for your Windows account). Enter a new one to replace it."
            : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                ? "Using the OPENAI_API_KEY environment variable. Enter a key to save one instead."
                : "No key saved. Keys are encrypted for your Windows account and only step text is sent, never screenshots.";
    }

    private string? GetApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            return ApiKeyBox.Password.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.OpenAiApiKeyProtected))
        {
            try
            {
                return Unprotect(_settings.OpenAiApiKeyProtected);
            }
            catch (CryptographicException)
            {
                // Saved under a different Windows account or machine; fall back to the environment.
            }
        }

        return Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }

    private static string Protect(string value)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string value)
    {
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
