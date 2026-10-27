using System;
using System.Collections.Generic;

namespace ClaudeVisualChat;

/// <summary>
/// How the touch layout's model sheet groups the models the claude offers. The main ones are the first model of each
/// family (Opus, Fable, Sonnet, Haiku…), in the claude's order and without its <c>default</c> entry; the rest come under
/// "Other models".
/// </summary>
internal static class ModelGroups
{
    private const string DefaultValue = "default";
    private const string IdPrefix = "claude-";

    /// <summary>
    /// Splits the models in the main ones and the others, each in the order received. A model belongs to the family named
    /// by the second piece of its resolved id (<c>claude-opus-5-5</c> is <c>opus</c>); one whose id does not start with
    /// <c>claude-</c> has none, and goes to the others. The <c>default</c> entry is in neither: it names a model that is also
    /// in the list.
    /// </summary>
    internal static (IReadOnlyList<ChatModel> Main, IReadOnlyList<ChatModel> Others) Split(IReadOnlyList<ChatModel> models)
    {
        if (models == null) throw new ArgumentNullException(nameof(models));
        var main = new List<ChatModel>();
        var others = new List<ChatModel>();
        var families = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (model.Value == DefaultValue) continue;
            var family = FamilyOf(model.ResolvedModel);
            if (family != null && families.Add(family)) main.Add(model);
            else others.Add(model);
        }
        return (main, others);
    }

    /// <summary>The family of a resolved id: <c>opus</c> for <c>claude-opus-5-5</c>; null when the id has none.</summary>
    private static string? FamilyOf(string? resolvedModel)
    {
        if (resolvedModel == null || !resolvedModel.StartsWith(IdPrefix, StringComparison.Ordinal)) return null;
        var rest = resolvedModel.Substring(IdPrefix.Length);
        var end = rest.IndexOf('-');
        var family = end < 0 ? rest : rest.Substring(0, end);
        return family.Length == 0 ? null : family;
    }
}
