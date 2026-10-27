using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The model sheet of the touch layout, in the style of the "Select model" sheet of Anthropic's Claude app; the model pill of
/// the reply box opens it (<see cref="ChatComposer.ModelSheetRequested"/>). A block of the main models (<see cref="ModelGroups"/>),
/// each with its name and its description (in the language of <see cref="ChatStrings.ModelDescriptions"/>) and the current one
/// checked; under it, for a model that takes an effort, a block with the "Effort" row, which shows the level (the one asked for
/// while it waits, else the one the claude reports) and opens a page with the model's levels, the current one checked; then the
/// others, by name. Picking a model asks for it (<see cref="ChatConversation.SetModel"/>) and closes the sheet; picking a level
/// asks for it (<see cref="ChatConversation.SetEffort"/>) and goes back to the models. It shows what the claude reports, so it
/// is drawn again as that changes.
/// </summary>
internal sealed class ModelSheet : ChatSheet
{
    /// <summary>The page with the levels of the effort.</summary>
    private const string EffortPage = "effort";

    private const double MainRowHeight = 64;
    private const double RowHeight = 52;

    /// <summary>There is a model to pick: a list with nothing but the <c>default</c> entry, which the sheet leaves out, has none.</summary>
    protected override bool CanOpen(ChatConversation conversation)
    {
        if (conversation.AvailableModels is not { Count: > 0 } models) return false;
        var (main, others) = ModelGroups.Split(models);
        return main.Count + others.Count > 0;
    }

    protected override bool HasPage(ChatConversation conversation, string page) =>
        page != EffortPage || conversation.CurrentModel is { EffortLevels.Count: > 0 };

    protected override bool Shows(string? propertyName) =>
        propertyName is nameof(ChatConversation.Model) or nameof(ChatConversation.CurrentModel)
            or nameof(ChatConversation.AvailableModels) or nameof(ChatConversation.Effort)
            or nameof(ChatConversation.RequestedEffort);

    protected override string Title(ChatStrings strings) => Page == EffortPage ? strings.Effort : strings.SelectModelTitle;

    protected override void Fill(Panel body, ChatConversation conversation, ChatStrings strings)
    {
        if (Page == EffortPage) FillLevels(body, conversation, strings);
        else FillModels(body, conversation, strings);
    }

    private void FillModels(Panel body, ChatConversation conversation, ChatStrings strings)
    {
        var current = conversation.CurrentModel;
        var (main, others) = ModelGroups.Split(conversation.AvailableModels ?? Array.Empty<ChatModel>());
        if (main.Count > 0)
        {
            var rows = new List<Control>();
            foreach (var model in main)
            {
                var description = strings.ModelDescription(model.Description);
                rows.Add(Row(model.DisplayName, () => Pick(conversation, model), MainRowHeight, description,
                    check: ReferenceEquals(model, current)));
            }
            body.Children.Add(Group(rows));
        }
        if (current is { EffortLevels.Count: > 0 })
        {
            var level = strings.EffortLabel(conversation.RequestedEffort ?? conversation.Effort);
            body.Children.Add(Group(new[] { Row(strings.Effort, () => ShowPage(EffortPage), RowHeight, value: level, opens: true) }));
        }
        if (others.Count > 0)
        {
            if (main.Count > 0) body.Children.Add(Label(strings.OtherModels));
            var rows = new List<Control>();
            foreach (var model in others) rows.Add(Row(model.DisplayName, () => Pick(conversation, model), RowHeight, check: ReferenceEquals(model, current)));
            body.Children.Add(Group(rows));
        }
    }

    private void FillLevels(Panel body, ChatConversation conversation, ChatStrings strings)
    {
        var shown = conversation.RequestedEffort ?? conversation.Effort;
        var rows = new List<Control>();
        foreach (var level in conversation.CurrentModel?.EffortLevels ?? Array.Empty<string>())
            rows.Add(Row(strings.EffortLabel(level), () => PickLevel(conversation, level), RowHeight, check: level == shown));
        body.Children.Add(Group(rows));
    }

    private void Pick(ChatConversation conversation, ChatModel model)
    {
        Close();
        conversation.SetModel(model.Value);
    }

    private void PickLevel(ChatConversation conversation, string level)
    {
        conversation.SetEffort(level);
        ShowPage(null);
    }
}
