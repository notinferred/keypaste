using Keypaste.App.ViewModels;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>Opens a template's New item form and types its title, as a person does.</summary>
internal static class NewItemForm
{
    /// <param name="entries">The Items screen.</param>
    /// <param name="title">The title; empty leaves it as it opens.</param>
    /// <param name="template">The template whose form opens, as its tile, the list's "+" or a menu opens it.</param>
    /// <returns>The open form.</returns>
    internal static NewItemViewModel Open(EntriesViewModel entries, string title = "", ItemTemplate template = ItemTemplate.Login)
    {
        entries.BeginAddOfKindCommand.Press(template);
        var form = entries.NewItem ?? throw new Xunit.Sdk.XunitException($"New item did not open: {entries.Error}");

        if (title.Length > 0)
        {
            form.Title = title;
        }

        return form;
    }
}
