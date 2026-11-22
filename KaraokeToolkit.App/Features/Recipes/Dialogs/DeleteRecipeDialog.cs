using System.Threading.Tasks;
using Avalonia.Controls;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Recipes.Dialogs;

internal static class DeleteRecipeDialog
{
    public static Task<bool> ShowAsync(Window owner, string recipeName) =>
        PromptDialog.ConfirmAsync(owner, "Delete saved recipe?",
            $"“{recipeName}” and its unsaved edits will be removed from the builder.",
            "Delete Recipe", destructive: true);
}
