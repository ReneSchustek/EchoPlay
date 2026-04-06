using System.Runtime.CompilerServices;

// Erlaubt EchoPlay.App.Tests den Zugriff auf internal-Mitglieder –
// notwendig, damit ViewModel-Hilfsmethoden (z.B. BuildAutoLookupQuery)
// direkt in Unit-Tests aufgerufen werden können, ohne sie public zu machen.
[assembly: InternalsVisibleTo("EchoPlay.App.Tests")]
