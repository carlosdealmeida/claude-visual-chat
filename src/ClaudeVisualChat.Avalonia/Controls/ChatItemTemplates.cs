using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The item templates of the theme as one template: the first one that matches the item builds it. A control is
/// reused for another item only when the same template built it, so a recycled container never shows a tool call in
/// the control of a message.
/// </summary>
internal sealed class ChatItemTemplates : IRecyclingDataTemplate
{
    private readonly ConditionalWeakTable<Control, IDataTemplate> _builtBy = new ConditionalWeakTable<Control, IDataTemplate>();

    [Content]
    public List<IDataTemplate> Templates { get; } = new List<IDataTemplate>();

    public bool Match(object? data) => Find(data) != null;

    public Control? Build(object? param) => Build(param, null);

    public Control? Build(object? data, Control? existing)
    {
        var template = Find(data);
        if (template == null) return null;
        if (existing != null && _builtBy.TryGetValue(existing, out var owner) && ReferenceEquals(owner, template))
            return existing;
        var control = template.Build(data);
        if (control != null)
        {
            _builtBy.Remove(control);
            _builtBy.Add(control, template);
        }
        return control;
    }

    private IDataTemplate? Find(object? data)
    {
        foreach (var template in Templates)
        {
            if (template.Match(data)) return template;
        }
        return null;
    }
}
