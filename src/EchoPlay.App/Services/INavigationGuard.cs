using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Wird von ViewModels implementiert, die das Verlassen einer Seite blockieren oder
    /// bestätigen lassen wollen (z.B. ungespeicherte Änderungen). Das <c>MainWindow</c>
    /// prüft beim Navigationsanfrage, ob das aktuelle Page-ViewModel dieses Interface
    /// implementiert, und ruft <see cref="CanLeaveAsync"/> auf.
    /// </summary>
    public interface INavigationGuard
    {
        /// <summary>
        /// Prüft, ob die aktuelle Seite verlassen werden darf.
        /// </summary>
        /// <returns><c>true</c> wenn die Navigation fortgesetzt werden darf.</returns>
        Task<bool> CanLeaveAsync();
    }
}
