using System;
using System.Collections.Generic;

namespace MedCompanion.Services.Backup
{
    public enum BackupPhase
    {
        Verification,
        Analyse,
        Copie,
        Nettoyage,
        Termine
    }

    /// <summary>
    /// État d'avancement transmis à l'interface. Instance immuable recréée à chaque envoi :
    /// elle traverse la frontière entre le thread de sauvegarde et le thread graphique.
    /// </summary>
    public sealed class BackupProgress
    {
        public BackupPhase Phase { get; init; }
        public string Message { get; init; } = "";
        public string CurrentFile { get; init; } = "";
        public int FilesDone { get; init; }
        public int FilesTotal { get; init; }
        public long BytesDone { get; init; }
        public long BytesTotal { get; init; }
        public TimeSpan Elapsed { get; init; }
        public TimeSpan? Eta { get; init; }

        public double Percent =>
            BytesTotal > 0 ? Math.Min(100d, BytesDone * 100d / BytesTotal)
            : FilesTotal > 0 ? Math.Min(100d, FilesDone * 100d / FilesTotal)
            : 0d;
    }

    public sealed class BackupResult
    {
        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string? Error { get; set; }

        /// <summary>Fichiers réellement écrits sur le disque de sauvegarde.</summary>
        public int FilesCopied { get; set; }

        /// <summary>Fichiers dont la version précédente a été mise de côté avant écrasement.</summary>
        public int FilesVersioned { get; set; }

        /// <summary>Fichiers déjà à jour, non recopiés.</summary>
        public int FilesUpToDate { get; set; }

        /// <summary>Fichiers ignorés parce qu'ils étaient en cours d'écriture.</summary>
        public int FilesInUse { get; set; }

        public int FilesFailed { get; set; }
        public long BytesCopied { get; set; }
        public TimeSpan Duration { get; set; }
        public string? JournalPath { get; set; }
        public List<string> Warnings { get; } = new();

        public string Resume()
        {
            if (!Success)
                return Cancelled ? "interrompue" : $"échec — {Error}";

            if (FilesCopied == 0)
                return $"aucun changement ({Duration.TotalSeconds:N0} s)";

            var mo = BytesCopied / 1024d / 1024d;
            var txt = $"{FilesCopied} fichier(s), {mo:N1} Mo en {Duration.TotalSeconds:N0} s";
            if (FilesFailed > 0) txt += $" — {FilesFailed} en échec";
            else if (FilesInUse > 0) txt += $" — {FilesInUse} occupé(s), repris plus tard";
            return txt;
        }
    }
}
