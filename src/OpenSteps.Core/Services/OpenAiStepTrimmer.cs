using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>
/// Asks an OpenAI chat model which steps are noise. Only step text and window/element metadata are sent; screenshots stay local.
/// </summary>
public sealed class OpenAiStepTrimmer
{
    public const string DefaultModel = "gpt-5-mini";

    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    private const string SystemPrompt =
        "You edit recorded click-by-click Windows workflows into concise how-to guides. " +
        "Given the recorded steps, choose which ones to REMOVE so that only the core steps a reader needs remain. " +
        "Remove: accidental or repeated clicks, focus clicks on empty space, navigation keystrokes, clicks into a field that the next typing step already covers, " +
        "and detours that are undone later. Keep: every step that changes state, opens something the next step depends on, enters data, or confirms/saves. " +
        "When unsure, keep the step. " +
        "Respond with JSON only, shaped as {\"remove\":[{\"n\":<step number>,\"reason\":\"<short reason>\"}]}.";

    private readonly HttpClient _httpClient;

    public OpenAiStepTrimmer(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<StepTrimSuggestion>> SuggestRemovalsAsync(
        string guideTitle,
        IReadOnlyList<RecordedStep> steps,
        string apiKey,
        string? model,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim(),
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = BuildUserPrompt(guideTitle, steps) }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI request failed ({(int)response.StatusCode}): {ExtractErrorMessage(responseText)}");
        }

        var content = JsonNode.Parse(responseText)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("OpenAI returned an empty response.");
        }

        return ParseSuggestions(content, steps);
    }

    public static string BuildUserPrompt(string guideTitle, IReadOnlyList<RecordedStep> steps)
    {
        var items = new JsonArray();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var item = new JsonObject
            {
                ["n"] = i + 1,
                ["action"] = step.ActionType.ToString(),
                ["title"] = step.DisplayTitle
            };
            AddIfPresent(item, "description", step.UserDescription);
            AddIfPresent(item, "app", step.ProcessName);
            AddIfPresent(item, "window", step.WindowTitle);
            AddIfPresent(item, "element", step.ElementName);
            AddIfPresent(item, "control", step.ControlType);
            AddIfPresent(item, "key", step.ShortcutName ?? step.SpecialKeyName);
            items.Add(item);
        }

        return $"Guide title: {guideTitle}\nSteps:\n{items.ToJsonString()}";
    }

    public static IReadOnlyList<StepTrimSuggestion> ParseSuggestions(string content, IReadOnlyList<RecordedStep> steps)
    {
        var suggestions = new List<StepTrimSuggestion>();
        var seen = new HashSet<int>();
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("remove", out var remove) || remove.ValueKind != JsonValueKind.Array)
        {
            return suggestions;
        }

        foreach (var entry in remove.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("n", out var numberElement)
                || numberElement.ValueKind != JsonValueKind.Number
                || !numberElement.TryGetInt32(out var number))
            {
                continue;
            }

            if (number < 1 || number > steps.Count || !seen.Add(number))
            {
                continue;
            }

            var step = steps[number - 1];
            if (StepTrimmer.IsProtected(step))
            {
                continue;
            }

            var reason = entry.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String
                ? reasonElement.GetString()
                : null;
            suggestions.Add(new StepTrimSuggestion(step.Id, string.IsNullOrWhiteSpace(reason) ? "Suggested by AI" : $"AI: {reason}"));
        }

        return suggestions;
    }

    private static void AddIfPresent(JsonObject item, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            item[name] = value;
        }
    }

    private static string ExtractErrorMessage(string responseText)
    {
        try
        {
            return JsonNode.Parse(responseText)?["error"]?["message"]?.GetValue<string>() ?? responseText;
        }
        catch (JsonException)
        {
            return responseText;
        }
    }
}
