using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

public sealed record StepTrimSuggestion(Guid StepId, string Reason);

public static class StepTrimmer
{
    private const int SamePointTolerancePx = 12;

    private static readonly HashSet<string> NavigationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tab",
        "Backspace",
        "Left Arrow",
        "Up Arrow",
        "Right Arrow",
        "Down Arrow"
    };

    private static readonly HashSet<string> ContainerControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pane",
        "Window",
        "Document",
        "Group",
        "Custom"
    };

    /// <summary>
    /// Suggests low-value steps to remove using local rules only. Steps the user added or edited are never suggested.
    /// </summary>
    public static IReadOnlyList<StepTrimSuggestion> SuggestRemovals(IReadOnlyList<RecordedStep> steps)
    {
        var suggestions = new List<StepTrimSuggestion>();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (IsProtected(step))
            {
                continue;
            }

            var next = i + 1 < steps.Count ? steps[i + 1] : null;
            var reason = GetRemovalReason(step, next);
            if (reason is not null)
            {
                suggestions.Add(new StepTrimSuggestion(step.Id, reason));
            }
        }

        return suggestions;
    }

    public static bool IsProtected(RecordedStep step)
    {
        return step.ActionType == StepActionType.Manual
            || !string.IsNullOrWhiteSpace(step.UserDescription)
            || (!string.IsNullOrWhiteSpace(step.UserTitle) && step.UserTitle != step.GeneratedTitle);
    }

    private static string? GetRemovalReason(RecordedStep step, RecordedStep? next)
    {
        if (step.ActionType == StepActionType.SpecialKey
            && step.SpecialKeyName is { } key
            && NavigationKeys.Contains(key))
        {
            return $"Navigation keystroke ({key})";
        }

        if (!IsPointerAction(step) || next is null)
        {
            return null;
        }

        if (next.ActionType == StepActionType.TextEntry && SameApp(step, next) && IsSameInputTarget(step, next))
        {
            return "Click into a field that the next typing step already covers";
        }

        if (IsPointerAction(next) && SameApp(step, next) && IsSameTarget(step, next))
        {
            return "Repeated click on the same target";
        }

        if (step.ActionType == StepActionType.Click
            && step.UiAutomationQuality != UiAutomationQuality.UiAutomationFailed
            && string.IsNullOrWhiteSpace(step.ElementName)
            && (string.IsNullOrWhiteSpace(step.ControlType) || ContainerControlTypes.Contains(step.ControlType))
            && SameApp(step, next)
            && string.Equals(step.WindowTitle, next.WindowTitle, StringComparison.Ordinal))
        {
            return "Click on empty space in the window (likely a focus click)";
        }

        return null;
    }

    private static bool IsPointerAction(RecordedStep step)
    {
        return step.ActionType is StepActionType.Click or StepActionType.DoubleClick or StepActionType.RightClick;
    }

    private static bool SameApp(RecordedStep a, RecordedStep b)
    {
        return string.Equals(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameInputTarget(RecordedStep click, RecordedStep typing)
    {
        if (string.Equals(click.ControlType, "Edit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(click.ControlType, "ComboBox", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(click.ElementName)
            && string.Equals(click.ElementName, typing.InputTargetName ?? typing.ElementName, StringComparison.Ordinal);
    }

    private static bool IsSameTarget(RecordedStep a, RecordedStep b)
    {
        // A right-click followed by a click is usually "open context menu, pick item"; keep both.
        if (a.ActionType == StepActionType.RightClick)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(a.ElementName) || !string.IsNullOrWhiteSpace(b.ElementName))
        {
            return string.Equals(a.ElementName, b.ElementName, StringComparison.Ordinal)
                && string.Equals(a.ControlType, b.ControlType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.WindowTitle, b.WindowTitle, StringComparison.Ordinal);
        }

        return string.Equals(a.WindowTitle, b.WindowTitle, StringComparison.Ordinal)
            && Math.Abs(a.ClickX - b.ClickX) <= SamePointTolerancePx
            && Math.Abs(a.ClickY - b.ClickY) <= SamePointTolerancePx;
    }
}
