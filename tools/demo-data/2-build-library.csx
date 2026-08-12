#r "nuget: TagLibSharp, 2.3.0"
#nullable enable

// Baut die Demo-Bibliothek aus gemeinfreien Titeln.
//
// Struktur wie vom Einleser erwartet: Wurzel -> Serienordner -> Folgenordner -> Audiodatei.
// Das Cover der Serie liegt als cover.jpg direkt im Serienordner und zusätzlich als
// Kennzeichnung in jeder Tonspur.
//
// Aufruf: dotnet script 2-build-library.csx -- <Bibliothekswurzel> <Coverordner aus Schritt 1>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

string root = Args.Count > 0 ? Args[0] : @"C:\Hörspiele-Demo";
string coverSource = Args.Count > 1 ? Args[1] : Path.Combine(Path.GetTempPath(), "echoplay-demo", "cover");

// Sprachproben aus dem Windows-Bestand. Sie werden nicht veröffentlicht, sondern nur
// abgespielt, damit die Wiedergabeansicht etwas zu zeigen hat.
string[] audioSources =
[
    @"C:\Windows\ImmersiveControlPanel\SystemSettings\Assets\Aria.mp3",
    @"C:\Windows\ImmersiveControlPanel\SystemSettings\Assets\Guy.mp3",
    @"C:\Windows\ImmersiveControlPanel\SystemSettings\Assets\Jenny.mp3"
];

foreach (string source in audioSources)
{
    if (!File.Exists(source))
    {
        Console.WriteLine($"Tonquelle fehlt: {source}");
        return;
    }
}

// Die Anfangsbuchstaben sind bewusst breit gestreut, damit die Sprungleiste im Bild etwas
// zu zeigen hat.
(string Title, string Author, string[] Episodes)[] series =
[
    ("Alice im Wunderland", "Lewis Carroll",
        ["Hinab in den Kaninchenbau", "Der Teich der Tränen", "Ein verrücktes Teekränzchen", "Das Krocketfeld der Königin"]),
    ("Anna Karenina", "Leo Tolstoi",
        ["Ankunft in Moskau", "Der Ball", "Auf dem Gut", "Die Reise nach Italien"]),
    ("Das Dschungelbuch", "Rudyard Kipling",
        ["Mowglis Brüder", "Die Jagd des Kaa", "Tiger, Tiger", "Der weiße Seehund"]),
    ("Der Graf von Monte Christo", "Alexandre Dumas",
        ["Die Ankunft in Marseille", "Das Verlies", "Die Insel", "Die Rückkehr"]),
    ("Die drei Musketiere", "Alexandre Dumas",
        ["Der junge Gascogner", "Die Nadelstiche", "Die Diamanten der Königin", "Die Bastion"]),
    ("Die Insel des Doktor Moreau", "H. G. Wells",
        ["Rettung auf See", "Die Stimmen im Wald", "Das Gesetz", "Der Aufbruch"]),
    ("Die Schatzinsel", "Robert Louis Stevenson",
        ["Der alte Seebär", "Die Schwarze Marke", "Die Fahrt der Hispaniola", "Das Apfelfass", "Die Insel"]),
    ("Die Zeitmaschine", "H. G. Wells",
        ["Die Erfindung", "Das Jahr 802701", "Die Morlocks", "Das Ende der Zeit"]),
    ("Dracula", "Bram Stoker",
        ["Die Reise nach Transsilvanien", "Das Schloss", "Die Demeter", "Die Jagd"]),
    ("Emma", "Jane Austen",
        ["Die Kupplerin", "Das Porträt", "Der Ball in Highbury", "Box Hill"]),
    ("Frankenstein", "Mary Shelley",
        ["Im ewigen Eis", "Das Werk", "Die Kreatur spricht", "Die Verfolgung"]),
    ("Krieg der Welten", "H. G. Wells",
        ["Der Zylinder auf dem Feld", "Der Hitzestrahl", "Die Flucht aus London", "Das rote Kraut"]),
    ("Moby Dick", "Herman Melville",
        ["Loomings", "Die Pequod läuft aus", "Ahabs Schwur", "Die letzte Jagd"]),
    ("Nordsee-Geschichten", "Theodor Storm",
        ["Der Schimmelreiter", "Am Deich", "Die Halligen", "Sturmflut"]),
    ("Oliver Twist", "Charles Dickens",
        ["Im Armenhaus", "Der Weg nach London", "Fagins Bande", "Die Rückkehr"]),
    ("Peter Pan", "J. M. Barrie",
        ["Das Fenster im Kinderzimmer", "Nimmerland", "Die verlorenen Jungs", "Kapitän Hook"]),
    ("Robinson Crusoe", "Daniel Defoe",
        ["Der Sturm", "Die Insel", "Die Spur im Sand", "Freitag", "Die Rettung"]),
    ("Sherlock Holmes", "Arthur Conan Doyle",
        ["Eine Studie in Scharlachrot", "Das Zeichen der Vier", "Der Hund von Baskerville", "Das letzte Problem", "Das leere Haus", "Der Bund der Rothaarige"]),
    ("Tom Sawyer", "Mark Twain",
        ["Der Zaun", "Die Insel der Piraten", "Nachts auf dem Friedhof", "Die Höhle"]),
    ("Winnetou", "Karl May",
        ["Im Wilden Westen", "Die Begegnung", "Der Marterpfahl", "Der Bund"])
];

// Schutz vor dem Fehlgriff: Der Zielordner wird gelöscht, bevor er neu entsteht. Zeigt der
// Pfad auf eine echte Sammlung — ein Tippfehler genügt —, wäre sie weg. Ein vorhandener
// Ordner darf deshalb nur wenige Dateien enthalten, so wie ein früherer Demo-Lauf.
const int fileLimit = 400;

if (Directory.Exists(root))
{
    int existingFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
    if (existingFiles > fileLimit)
    {
        Console.WriteLine($"Abbruch: {root} enthält {existingFiles} Dateien.");
        Console.WriteLine($"Erwartet wird ein leerer Ordner oder ein früherer Demo-Lauf (höchstens {fileLimit} Dateien).");
        return;
    }

    Directory.Delete(root, recursive: true);
}
Directory.CreateDirectory(root);

int fileCount = 0;
int audioIndex = 0;
int missingCovers = 0;

foreach ((string title, string author, string[] episodes) in series)
{
    string seriesFolder = Path.Combine(root, title);
    Directory.CreateDirectory(seriesFolder);

    // Der Dateiname muss dem entsprechen, den Schritt 1 vergibt.
    string coverName = new string([.. title.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-')]).Replace(' ', '_');
    string coverFile = Path.Combine(coverSource, coverName + ".jpg");
    byte[]? coverBytes = File.Exists(coverFile) ? File.ReadAllBytes(coverFile) : null;

    if (coverBytes is not null)
    {
        File.WriteAllBytes(Path.Combine(seriesFolder, "cover.jpg"), coverBytes);
    }
    else
    {
        Console.WriteLine($"  Cover fehlt für {title} (gesucht: {coverFile})");
        missingCovers++;
    }

    for (int i = 0; i < episodes.Length; i++)
    {
        int number = i + 1;
        string episodeTitle = episodes[i];
        string episodeFolder = Path.Combine(seriesFolder, $"{number:000} - {episodeTitle}");
        Directory.CreateDirectory(episodeFolder);

        // Zwei Spuren je Folge, damit die Trackliste in der Detailansicht nicht leer ist.
        for (int track = 1; track <= 2; track++)
        {
            string source = audioSources[audioIndex++ % audioSources.Length];
            string target = Path.Combine(episodeFolder, $"{track:00} - {episodeTitle} (Teil {track}).mp3");
            File.Copy(source, target, overwrite: true);

            using TagLib.File tagFile = TagLib.File.Create(target);
            tagFile.Tag.Clear();
            tagFile.Tag.Album = title;
            tagFile.Tag.Title = $"{episodeTitle} (Teil {track})";
            tagFile.Tag.Performers = [author];
            tagFile.Tag.AlbumArtists = [author];
            tagFile.Tag.Track = (uint)track;
            tagFile.Tag.TrackCount = 2;
            tagFile.Tag.Disc = (uint)number;
            tagFile.Tag.Genres = ["Hörspiel"];
            tagFile.Tag.Year = 2026;

            if (coverBytes is not null)
            {
                tagFile.Tag.Pictures =
                [
                    new TagLib.Picture(new TagLib.ByteVector(coverBytes))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        MimeType = "image/jpeg"
                    }
                ];
            }

            tagFile.Save();
            fileCount++;
        }
    }
}

int episodeCount = series.Sum(s => s.Episodes.Length);
Console.WriteLine($"Demo-Bibliothek: {series.Length} Serien, {episodeCount} Folgen, {fileCount} Dateien");
Console.WriteLine($"Ablage: {root}");

if (missingCovers > 0)
{
    Console.WriteLine($"Ohne Cover: {missingCovers} Serien — lief Schritt 1 mit demselben Zielordner?");
}
