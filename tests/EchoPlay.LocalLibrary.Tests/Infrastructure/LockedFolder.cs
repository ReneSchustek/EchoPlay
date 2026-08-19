using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EchoPlay.LocalLibrary.Tests.Infrastructure
{
    /// <summary>
    /// Ein Ordner, dessen Inhalt sich für die Dauer des Blocks nicht auflisten lässt.
    /// </summary>
    /// <remarks>
    /// Das ist der Zustand eines getrennten Netzlaufwerks oder eines Ordners, den ein
    /// anderes Konto angelegt hat: <see cref="Directory.Exists(string)"/> sagt weiterhin
    /// ja, jeder Zugriff auf den Inhalt scheitert. Anders ist dieser Zweig nicht zu
    /// erreichen — ein nicht vorhandener Ordner wird vorher abgefangen.
    ///
    /// Die Sperre wird beim Freigeben in jedem Fall zurückgenommen; bleibt sie stehen,
    /// könnte der Ordner nicht mehr gelöscht werden.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal sealed class LockedFolder : IDisposable
    {
        private readonly DirectoryInfo _folder;
        private readonly FileSystemAccessRule _rule;

        private LockedFolder(DirectoryInfo folder, FileSystemAccessRule rule)
        {
            _folder = folder;
            _rule = rule;
        }

        /// <summary>Der Pfad des gesperrten Ordners.</summary>
        public string Path => _folder.FullName;

        /// <summary>Legt einen Ordner an und entzieht dem laufenden Konto das Auflisten.</summary>
        /// <param name="parent">Übergeordneter Ordner.</param>
        /// <param name="name">Name des zu sperrenden Ordners.</param>
        /// <returns>Der gesperrte Ordner; die Sperre endet beim Freigeben.</returns>
        public static LockedFolder Create(string parent, string name = "gesperrt")
        {
            DirectoryInfo folder = Directory.CreateDirectory(System.IO.Path.Combine(parent, name));

            FileSystemAccessRule rule = new(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ListDirectory | FileSystemRights.ReadData,
                AccessControlType.Deny);

            DirectorySecurity security = folder.GetAccessControl();
            security.AddAccessRule(rule);
            folder.SetAccessControl(security);

            return new LockedFolder(folder, rule);
        }

        /// <summary>Nimmt die Sperre zurück.</summary>
        public void Dispose()
        {
            if (!_folder.Exists)
            {
                return;
            }

            DirectorySecurity security = _folder.GetAccessControl();
            security.RemoveAccessRuleAll(_rule);
            _folder.SetAccessControl(security);
        }
    }
}
