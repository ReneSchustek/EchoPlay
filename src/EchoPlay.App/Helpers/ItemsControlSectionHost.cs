using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Zeigt Abschnitte in einem <see cref="ItemsControl"/> an.
    /// <para>
    /// Bewusst ohne eigene Entscheidung: Was angezeigt wird und wohin gesprungen wird, steht in
    /// <see cref="LetterSectionSplitHandler{T}"/>. Hier bleibt nur, was ohne laufende
    /// Oberfläche ohnehin nicht zu prüfen wäre.
    /// </para>
    /// </summary>
    public sealed class ItemsControlSectionHost : ILetterSectionHost
    {
        private readonly ItemsControl _host;
        private IReadOnlyList<LetterSection> _sections = [];

        /// <summary>
        /// Bindet den Wirt an ein Steuerelement.
        /// </summary>
        /// <param name="host">Das Steuerelement, das die Abschnitte anzeigt.</param>
        public ItemsControlSectionHost(ItemsControl host) => _host = host;

        /// <inheritdoc/>
        public IReadOnlyList<LetterSection> Sections
        {
            get => _sections;
            set
            {
                _sections = value;
                _host.ItemsSource = value;
            }
        }

        /// <inheritdoc/>
        public bool TryBringIntoView(LetterSection section)
        {
            if (_host.ContainerFromItem(section) is not FrameworkElement container)
            {
                return false;
            }

            container.StartBringIntoView();
            return true;
        }
    }
}
