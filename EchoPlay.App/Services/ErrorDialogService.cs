using EchoPlay.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Zeigt Fehler-Dialoge über den WinUI-3-ContentDialog an.
    /// Content-Aufbau liegt in <see cref="ErrorDialogContent.Build"/> — testbar ohne XamlRoot.
    /// </summary>

    public sealed class ErrorDialogService : IErrorDialogService
    {
        private readonly Func<XamlRoot?> _xamlRootProvider;

        /// <summary>
        /// Standard-Konstruktor: nutzt <see cref="App.MainWindow"/> zur Laufzeit.
        /// </summary>
        public ErrorDialogService()
            : this(static () => App.MainWindow?.Content?.XamlRoot)
        {
        }

        /// <summary>
        /// Test-Konstruktor: erlaubt das Einsetzen eines Fake-XamlRoot-Providers
        /// (auch null fuer Pre-MainWindow-Szenarien).
        /// </summary>
        internal ErrorDialogService(Func<XamlRoot?> xamlRootProvider)
        {
            _xamlRootProvider = xamlRootProvider ?? throw new ArgumentNullException(nameof(xamlRootProvider));
        }

        /// <inheritdoc />
        public async Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            ErrorDialogContent content = ErrorDialogContent.Build(title, message);

            // Defense-in-Depth: bei Startup-Failures vor abgeschlossener MainWindow-Init
            // (siehe App.xaml.cs Fatal-Pfade) ist MainWindow oder XamlRoot null. Statt
            // NullReferenceException im Error-Service Trace-Fallback (analog Brief 305 Splash).
            XamlRoot? xamlRoot = _xamlRootProvider();
            if (xamlRoot is null)
            {
                Trace.WriteLine($"ErrorDialogService: {content.Title} — {content.Message} (MainWindow nicht verfuegbar)");
                return;
            }

            ContentDialog dialog = new()
            {
                Title = content.Title,
                Content = content.Message,
                CloseButtonText = content.CloseButtonText,
                XamlRoot = xamlRoot
            };

            ContentDialogDragHelper.MakeDraggable(dialog);
            _ = await dialog.ShowAsync();
        }
    }
}
