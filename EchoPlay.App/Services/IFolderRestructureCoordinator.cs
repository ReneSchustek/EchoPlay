using EchoPlay.App.Models;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Koordiniert den Ordnerstruktur-Assistenten für eine lokale Serie. Kapselt
    /// AppSettings-Lookup für das Ordnermuster, den Aufruf des
    /// <see cref="EchoPlay.LocalLibrary.Abstractions.IFolderRestructureService"/>
    /// und das Mapping auf das App-Display-Modell <see cref="RestructurePreviewDisplay"/>.
    /// Hält keinen UI-State – das ViewModel kennt nur die zwei Methoden hier.
    /// </summary>
    public interface IFolderRestructureCoordinator
    {
        /// <summary>
        /// Analysiert den angegebenen Serienordner und liefert eine Vorschau der
        /// geplanten Verschiebungen. Liefert <see langword="null"/>, wenn der Ordner
        /// nicht existiert oder keine verschiebbaren Dateien gefunden wurden.
        /// </summary>
        /// <param name="seriesFolderPath">Absoluter Pfad des Serienordners.</param>
        Task<RestructurePreviewDisplay?> AnalyzeAsync(string seriesFolderPath);

        /// <summary>
        /// Führt den zuvor analysierten Restrukturierungs-Vorgang aus und liefert
        /// die Anzahl tatsächlich verschobener Dateien.
        /// </summary>
        /// <param name="preview">Die App-Display-Vorschau, die <see cref="AnalyzeAsync"/> erzeugt hat.</param>
        Task<int> ExecuteAsync(RestructurePreviewDisplay preview);
    }
}
