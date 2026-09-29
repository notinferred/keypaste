using Keypaste.App.ViewModels;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>Opens New item and picks its title and folder from a path, as a person picks them (N.4).</summary>
internal static class NewItemForm
{
    /// <param name="entries">The Items screen.</param>
    /// <param name="path">The group, which must exist, and the title, joined by a slash; empty leaves both as they open.</param>
    /// <returns>The open form.</returns>
    internal static NewItemViewModel Open(EntriesViewModel entries, string path = "")
    {
        entries.BeginAddCommand.Execute(null);
        var form = entries.NewItem ?? throw new Xunit.Sdk.XunitException($"New item did not open: {entries.Error}");

        if (path.Length > 0)
        {
            var slash = path.LastIndexOf('/');
            var group = slash < 0 ? string.Empty : path[..slash];
            form.Title = slash < 0 ? path : path[(slash + 1)..];
            form.Folder = form.Folders.SingleOrDefault(folder => folder.Path == group)
                ?? throw new Xunit.Sdk.XunitException($"the folder picker offers no '{group}'");
        }

        return form;
    }
}
