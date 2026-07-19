using System.Diagnostics.CodeAnalysis;

// xUnit-InlineData liefert Theorie-Parameter ausschließlich als primitive Werte;
// die getesteten Hilfsmethoden nehmen bewusst einen string (nicht Uri) entgegen.
[assembly: SuppressMessage(
    "Design",
    "CA1054:URI-like parameters should not be strings",
    Scope = "member",
    Target = "~M:EchoPlay.App.Tests.Services.BackgroundProviderIdServiceTests.ExtractITunesCollectionId_ValidUrl_ReturnsId(System.String,System.String)",
    Justification = "xUnit-InlineData: string statt Uri, siehe Methodenkontrakt der zu testenden Hilfsmethode.")]

[assembly: SuppressMessage(
    "Design",
    "CA1054:URI-like parameters should not be strings",
    Scope = "member",
    Target = "~M:EchoPlay.App.Tests.Services.BackgroundProviderIdServiceTests.ExtractITunesCollectionId_InvalidUrl_ReturnsNull(System.String)",
    Justification = "xUnit-InlineData: string statt Uri, siehe Methodenkontrakt der zu testenden Hilfsmethode.")]
