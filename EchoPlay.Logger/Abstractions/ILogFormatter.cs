using EchoPlay.Logger.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchoPlay.Logger.Abstractions
{
    /// <summary>
    /// Definiert die Fähigkeit, einen Log-Eintrag in einen String zu formatieren.
    /// </summary>
    public interface ILogFormatter
    {
        /// <summary>
        /// Formatiert einen Log-Eintrag als lesbaren String.
        /// </summary>
        /// <param name="entry">Der zu formatierende Eintrag.</param>
        /// <returns>Der formatierte String.</returns>
        string Format(LogEntry entry);
    }
}