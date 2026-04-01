using System;
using System.Threading.Tasks;

namespace MedCompanion.Services.Voice
{
    /// <summary>
    /// Interface pour les services de saisie vocale (STT)
    /// </summary>
    public interface IVoiceInputService
    {
        /// <summary>
        /// Indique si l'enregistrement est actif
        /// </summary>
        bool IsRecording { get; }

        /// <summary>
        /// Indique si le service est disponible (ex: Handy installé)
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Raccourci clavier configuré (ex: "Ctrl+Shift+H")
        /// </summary>
        string Hotkey { get; set; }

        /// <summary>
        /// Démarre l'enregistrement vocal
        /// </summary>
        Task StartRecordingAsync();

        /// <summary>
        /// Arrête l'enregistrement vocal
        /// </summary>
        Task StopRecordingAsync();

        /// <summary>
        /// Bascule l'état d'enregistrement (start/stop)
        /// </summary>
        Task ToggleRecordingAsync();

        /// <summary>
        /// Événement déclenché quand l'enregistrement démarre
        /// </summary>
        event EventHandler? RecordingStarted;

        /// <summary>
        /// Événement déclenché quand l'enregistrement s'arrête
        /// </summary>
        event EventHandler? RecordingStopped;
    }
}
