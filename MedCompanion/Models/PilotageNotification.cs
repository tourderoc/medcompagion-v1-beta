using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente une notification envoyée du médecin vers les parents via Firebase
    /// </summary>
    public class PilotageNotification
    {
        /// <summary>
        /// ID unique de la notification
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Type de notification
        /// </summary>
        public NotificationType Type { get; set; } = NotificationType.Quick;

        /// <summary>
        /// Titre de la notification (affiché dans la push notification)
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Corps de la notification
        /// </summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// ID du parent destinataire (ou "all" pour broadcast)
        /// </summary>
        public string TargetParentId { get; set; } = string.Empty;

        /// <summary>
        /// Token ID associé au parent (pour retrouver le lien)
        /// </summary>
        public string? TokenId { get; set; }

        /// <summary>
        /// ID du message auquel cette notification répond (optionnel)
        /// </summary>
        public string? ReplyToMessageId { get; set; }

        /// <summary>
        /// Date de création
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Indique si la notification a été lue
        /// </summary>
        public bool Read { get; set; } = false;

        /// <summary>
        /// Nom du médecin expéditeur
        /// </summary>
        public string SenderName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Types de notifications
    /// </summary>
    public enum NotificationType
    {
        /// <summary>
        /// Notification de réponse email envoyée
        /// </summary>
        EmailReply,

        /// <summary>
        /// Réponse rapide sans email (RDV, info courte)
        /// </summary>
        Quick,

        /// <summary>
        /// Information patient (ordonnance dispo, etc.)
        /// </summary>
        Info,

        /// <summary>
        /// Diffusion à tous les parents (vacances, fermeture, etc.)
        /// </summary>
        Broadcast
    }

    /// <summary>
    /// Templates de réponses rapides prédéfinies
    /// </summary>
    public static class QuickReplyTemplates
    {
        public static readonly (string Title, string Body)[] RdvTemplates = new[]
        {
            ("RDV confirmé", "Votre rendez-vous est confirmé. Consultez Doctolib pour les détails."),
            ("RDV à reprogrammer", "Merci de reprendre rendez-vous sur Doctolib."),
            ("RDV annulé", "Votre rendez-vous a été annulé. Contactez le cabinet pour plus d'informations.")
        };

        public static readonly (string Title, string Body)[] DoctoLibTemplates = new[]
        {
            ("Ordonnance disponible", "Une nouvelle ordonnance est disponible sur votre espace Doctolib."),
            ("Document disponible", "Un nouveau document est disponible sur votre espace Doctolib."),
            ("Résultats disponibles", "Vos résultats sont disponibles sur votre espace Doctolib.")
        };

        public static readonly (string Title, string Body)[] InfoTemplates = new[]
        {
            ("Message reçu", "Votre message a bien été reçu. Une réponse vous sera envoyée prochainement."),
            ("En attente d'informations", "Merci de nous fournir les informations complémentaires demandées."),
            ("Consultation nécessaire", "Une consultation est nécessaire pour traiter votre demande.")
        };

        public static readonly (string Title, string Body)[] BroadcastTemplates = new[]
        {
            ("Vacances du cabinet", "Le cabinet sera fermé du [DATE] au [DATE]. En cas d'urgence, contactez le 15."),
            ("Changement d'horaires", "Les horaires du cabinet ont été modifiés. Consultez Doctolib pour les nouveaux créneaux."),
            ("Information importante", "Une information importante concernant le cabinet...")
        };
    }
}
