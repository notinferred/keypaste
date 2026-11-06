using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The columns Trash's head and every row share.</summary>
internal sealed class TrashColumns : TableColumns
{
    public TrashColumns()
        : base(TableStates.TrashColumns, spacing: 16)
    {
    }
}
