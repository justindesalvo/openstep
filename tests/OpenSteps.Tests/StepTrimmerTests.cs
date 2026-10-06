using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class StepTrimmerTests
{
    [Fact]
    public void SuggestRemovals_FlagsRepeatedClickOnSameTarget_KeepsLast()
    {
        var first = Click("Save", "Button");
        var second = Click("Save", "Button");

        var suggestions = StepTrimmer.SuggestRemovals([first, second]);

        Assert.Equal([first.Id], suggestions.Select(s => s.StepId).ToArray());
    }

    [Fact]
    public void SuggestRemovals_FlagsClickIntoFieldFollowedByTyping()
    {
        var click = Click("Email", "Edit");
        var typing = new RecordedStep { ActionType = StepActionType.TextEntry, ProcessName = "app", InputTargetName = "Email", GeneratedTitle = "Type" };

        var suggestions = StepTrimmer.SuggestRemovals([click, typing]);

        Assert.Equal([click.Id], suggestions.Select(s => s.StepId).ToArray());
    }

    [Fact]
    public void SuggestRemovals_FlagsNavigationKeysButNotEnter()
    {
        var tab = Key("Tab");
        var enter = Key("Enter");

        var suggestions = StepTrimmer.SuggestRemovals([tab, enter]);

        Assert.Equal([tab.Id], suggestions.Select(s => s.StepId).ToArray());
    }

    [Fact]
    public void SuggestRemovals_FlagsFocusClickOnEmptyPane()
    {
        var focus = Click(null, "Pane");
        focus.UiAutomationQuality = UiAutomationQuality.GenericContainerOnly;
        var next = Click("OK", "Button");

        var suggestions = StepTrimmer.SuggestRemovals([focus, next]);

        Assert.Equal([focus.Id], suggestions.Select(s => s.StepId).ToArray());
    }

    [Fact]
    public void SuggestRemovals_KeepsDistinctClicksAndRightClickMenus()
    {
        var rightClick = Click("File.txt", "ListItem");
        rightClick.ActionType = StepActionType.RightClick;
        var menuItem = Click("File.txt", "ListItem");
        var other = Click("Rename", "MenuItem");

        Assert.Empty(StepTrimmer.SuggestRemovals([rightClick, menuItem, other]));
    }

    [Fact]
    public void SuggestRemovals_NeverFlagsManualOrUserEditedSteps()
    {
        var edited = Click("Save", "Button");
        edited.UserTitle = "Save the report";
        var described = Click("Save", "Button");
        described.UserDescription = "Important";
        var manual = StepEditorOperations.CreateManualStep();
        var last = Click("Save", "Button");

        Assert.Empty(StepTrimmer.SuggestRemovals([edited, described, manual, last]));
    }

    [Fact]
    public void OpenAiParseSuggestions_MapsNumbersAndSkipsInvalidOrProtected()
    {
        var a = Click("A", "Button");
        var b = Click("B", "Button");
        var manual = StepEditorOperations.CreateManualStep();
        var steps = new List<RecordedStep> { a, b, manual };

        var suggestions = OpenAiStepTrimmer.ParseSuggestions(
            """{"remove":[{"n":2,"reason":"duplicate"},{"n":2},{"n":3},{"n":9},{"n":"x"}]}""",
            steps);

        var only = Assert.Single(suggestions);
        Assert.Equal(b.Id, only.StepId);
        Assert.Equal("AI: duplicate", only.Reason);
    }

    [Fact]
    public void OpenAiBuildUserPrompt_DoesNotIncludeScreenshotPaths()
    {
        var step = Click("Save", "Button");
        step.ScreenshotPath = @"C:\secret\step.png";

        var prompt = OpenAiStepTrimmer.BuildUserPrompt("Guide", [step]);

        Assert.Contains("Save", prompt);
        Assert.DoesNotContain("step.png", prompt);
    }

    private static RecordedStep Click(string? element, string controlType)
    {
        return new RecordedStep
        {
            ActionType = StepActionType.Click,
            ProcessName = "app",
            WindowTitle = "Main",
            ElementName = element,
            ControlType = controlType,
            UsefulElementFound = element is not null,
            UiAutomationQuality = element is null ? UiAutomationQuality.UiAutomationFailed : UiAutomationQuality.UsefulElementFound,
            GeneratedTitle = "Click",
            UserTitle = "Click"
        };
    }

    private static RecordedStep Key(string key)
    {
        return new RecordedStep
        {
            ActionType = StepActionType.SpecialKey,
            SpecialKeyName = key,
            ProcessName = "app",
            GeneratedTitle = $"Press {key}",
            UserTitle = $"Press {key}"
        };
    }
}
