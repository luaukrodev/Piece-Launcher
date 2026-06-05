using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Piece_Launcher.ViewModels;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Piece_Launcher
{
    /// <summary>
    /// Given a view model, returns the corresponding view if possible.
    /// </summary>
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away.",
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            // Map view model full name to view full name:
            // Example: Piece_Launcher.ViewModels.Pages.HomePageViewModel
            // -> Piece_Launcher.Views.Pages.HomePage
            var vmFullName = param.GetType().FullName!;
            var viewName = vmFullName.Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
                                     .Replace("ViewModel", string.Empty, StringComparison.Ordinal);

            // Try to get the type directly, then fall back to searching loaded assemblies
            var type = Type.GetType(viewName);
            if (type == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(viewName);
                    if (type != null) break;
                }
            }

            if (type != null)
            {
                return (Control)Activator.CreateInstance(type)!;
            }

            return new TextBlock { Text = "Not Found: " + viewName };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
