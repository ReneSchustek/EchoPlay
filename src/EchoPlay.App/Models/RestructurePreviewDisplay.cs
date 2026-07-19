using System.Collections.Generic;
using System;

namespace EchoPlay.App.Models
{
    /// <summary>
    /// App-Display-Modell für die Ordnerstruktur-Vorschau. Hält intern noch eine
    /// Referenz auf das ursprüngliche LocalLibrary-Modell, damit der Restrukturierungs-Service
    /// beim Ausführen weiterhin die Original-Aktionen erhält. Aus Sicht von UI und Pages
    /// existiert nur dieses Wrapper-Modell.
    /// </summary>
    public sealed class RestructurePreviewDisplay
    {
        private readonly EchoPlay.LocalLibrary.Models.RestructurePreview _original;

        /// <summary>
        /// Erstellt das Wrapper-Modell aus einem LocalLibrary-Suchergebnis.
        /// Mappt jede Aktion sofort auf das schmale <see cref="RestructureActionDisplay"/>-Record,
        /// damit die UI keine LocalLibrary-Typen mehr sieht.
        /// </summary>
        /// <param name="original">Das vom Restrukturierungs-Service erzeugte Original-Preview.</param>
        public RestructurePreviewDisplay(EchoPlay.LocalLibrary.Models.RestructurePreview original)
        {
            ArgumentNullException.ThrowIfNull(original);
            _original = original;

            List<RestructureActionDisplay> displays = new(original.Actions.Count);
            foreach (EchoPlay.LocalLibrary.Models.RestructureAction action in original.Actions)
            {
                displays.Add(new RestructureActionDisplay(action.FileName, action.TargetFolderName));
            }
            Actions = displays;
        }

        /// <summary>Anzahl der Audiodateien, die verschoben werden sollen.</summary>
        public int FileCount => _original.FileCount;

        /// <summary>Anzahl der neuen Ordner, die angelegt werden.</summary>
        public int FolderCount => _original.FolderCount;

        /// <summary>True wenn keine verschiebbaren Dateien gefunden wurden.</summary>
        public bool IsEmpty => _original.IsEmpty;

        /// <summary>Die geplanten Verschiebe-Aktionen für die Vorschau-Anzeige.</summary>
        public IReadOnlyList<RestructureActionDisplay> Actions { get; }

        /// <summary>
        /// Originale LocalLibrary-Vorschau – nur innerhalb des App-Assemblies sichtbar,
        /// damit der Restrukturierungs-Service beim Ausführen das vollständige Modell erhält.
        /// </summary>
        internal EchoPlay.LocalLibrary.Models.RestructurePreview Original => _original;
    }
}
