using System.Diagnostics.CodeAnalysis;

// CA1515 (Consider making public types internal) ist für Library-Assemblies gedacht.
// EchoPlay.App ist ein WinUI-3-Desktop-EXE-Assembly, dessen XAML-Databinding,
// x:Class-Generator und ICommand-Bindings auf public Typen in ViewModels, Views,
// Controls, Models und Windows aufsetzen. Diese public-Pflicht ist framework-bedingt
// und nicht im eigenen Code auflösbar.
[assembly: SuppressMessage("Design", "CA1515:Consider making public types internal",
    Scope = "namespaceanddescendants", Target = "~N:EchoPlay.App.ViewModels",
    Justification = "WinUI-3-XAML-Databinding benoetigt public ViewModels.")]
[assembly: SuppressMessage("Design", "CA1515:Consider making public types internal",
    Scope = "namespaceanddescendants", Target = "~N:EchoPlay.App.Views",
    Justification = "WinUI-3-XAML-Code-Behind (x:Class) benoetigt public View-Klassen.")]
[assembly: SuppressMessage("Design", "CA1515:Consider making public types internal",
    Scope = "namespaceanddescendants", Target = "~N:EchoPlay.App.Controls",
    Justification = "WinUI-3-XAML-Code-Behind (x:Class) und DependencyProperty-Binding benoetigen public Control-Klassen.")]
[assembly: SuppressMessage("Design", "CA1515:Consider making public types internal",
    Scope = "namespaceanddescendants", Target = "~N:EchoPlay.App.Models",
    Justification = "WinUI-3-XAML-Databinding (DataTemplate DataType) verwendet Models direkt als Binding-Quelle.")]
[assembly: SuppressMessage("Design", "CA1515:Consider making public types internal",
    Scope = "type", Target = "~T:EchoPlay.App.App",
    Justification = "WinUI-3 Application-Klasse muss public sein (Entry-Point).")]
[assembly: SuppressMessage("Design", "CA1515:Consider making public types internal",
    Scope = "type", Target = "~T:EchoPlay.App.MainWindow",
    Justification = "WinUI-3 Window-Klasse mit x:Class-Binding muss public sein.")]
[assembly: SuppressMessage("Design", "CA1515:Consider making public types internal",
    Scope = "type", Target = "~T:EchoPlay.App.SplashWindow",
    Justification = "WinUI-3 Window-Klasse mit x:Class-Binding muss public sein.")]
[assembly: SuppressMessage("Design", "CA1515:Consider making public types internal",
    Scope = "namespaceanddescendants", Target = "~N:EchoPlay.App.Services",
    Justification = "Services werden von public ViewModels und Views in Konstruktor- und Property-Signaturen referenziert; inconsistent accessibility verhindert internal.")]
[assembly: SuppressMessage("Design", "CA1515:Consider making public types internal",
    Scope = "namespaceanddescendants", Target = "~N:EchoPlay.App.Helpers",
    Justification = "Helper-Klassen werden aus public Views/Controls/ViewModels verwendet und muessen daher public bleiben.")]
[assembly: SuppressMessage("Design", "CA1515:Consider making public types internal",
    Scope = "namespaceanddescendants", Target = "~N:EchoPlay.App.Infrastructure",
    Justification = "ObservableObject-Basisklasse und RelayCommand werden in public ViewModel- und Property-Signaturen verwendet; inconsistent accessibility verhindert internal.")]
