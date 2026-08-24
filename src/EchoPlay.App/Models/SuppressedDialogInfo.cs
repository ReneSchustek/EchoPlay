using EchoPlay.Core.Models;
using System;

namespace EchoPlay.App.Models
{
    /// <summary>
    /// Ein dauerhaft ausgeblendeter Dialog, wie ihn die Einstellungen auflisten.
    /// </summary>
    /// <param name="Key">Der ausgeblendete Dialog.</param>
    /// <param name="SuppressedAt">Zeitpunkt des Ausblendens (UTC).</param>
    public sealed record SuppressedDialogInfo(DialogKey Key, DateTime SuppressedAt);
}
