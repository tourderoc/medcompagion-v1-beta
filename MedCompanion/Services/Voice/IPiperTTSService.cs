using System;
using System.Threading.Tasks;

namespace MedCompanion.Services.Voice
{
    /// <summary>
    /// Interface pour le service de synthèse vocale Piper TTS
    /// Permet à Med de parler ses réponses à voix haute
    /// </summary>
    public interface IPiperTTSService
    {
        /// <summary>
        /// Indique si une lecture audio est en cours
        /// </summary>
        bool IsSpeaking { get; }

        /// <summary>
        /// Indique si le service est disponible (piper.exe et modèle trouvés)
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Chemin vers piper.exe
        /// </summary>
        string PiperExePath { get; set; }

        /// <summary>
        /// Chemin vers le modèle .onnx
        /// </summary>
        string ModelPath { get; set; }

        /// <summary>
        /// Vitesse de lecture (0.5 = lent, 1.0 = normal, 2.0 = rapide)
        /// </summary>
        float Speed { get; set; }

        /// <summary>
        /// Synthétise et lit le texte à voix haute
        /// </summary>
        /// <param name="text">Le texte à lire</param>
        Task SpeakAsync(string text);

        /// <summary>
        /// Arrête la lecture en cours
        /// </summary>
        Task StopAsync();
        
        /// <summary>
        /// Initialise une session de lecture en streaming (prépare les files d'attente)
        /// </summary>
        Task InitializeStreamAsync();

        /// <summary>
        /// Ajoute une phrase à la file d'attente de lecture en streaming
        /// </summary>
        Task EnqueuePhraseAsync(string phrase);

        /// <summary>
        /// Finalise la session de streaming et libère les ressources
        /// </summary>
        Task FinalizeStreamAsync();

        /// <summary>
        /// Vérifie si Piper est correctement configuré
        /// </summary>
        /// <returns>Tuple (success, errorMessage)</returns>
        (bool success, string? error) CheckConfiguration();

        /// <summary>
        /// Événement déclenché quand la lecture commence
        /// </summary>
        event EventHandler? SpeechStarted;

        /// <summary>
        /// Événement déclenché quand la lecture se termine
        /// </summary>
        event EventHandler? SpeechCompleted;

        /// <summary>
        /// Événement déclenché en cas d'erreur
        /// </summary>
        event EventHandler<string>? SpeechError;
    }
}
