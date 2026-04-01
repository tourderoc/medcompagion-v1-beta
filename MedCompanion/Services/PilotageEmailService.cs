using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service d'envoi d'emails pour le mode Pilotage
    /// Utilise SMTP Infomaniak pour contact@parentaile.fr
    /// </summary>
    public class PilotageEmailService
    {
        private readonly AppSettings _settings;

        public PilotageEmailService(AppSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Vérifie si la configuration SMTP est complète
        /// </summary>
        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_settings.SmtpHost)
                && !string.IsNullOrWhiteSpace(_settings.SmtpUsername)
                && !string.IsNullOrWhiteSpace(_settings.SmtpPassword)
                && !string.IsNullOrWhiteSpace(_settings.SmtpFromEmail);
        }

        /// <summary>
        /// Envoie un email avec pièces jointes optionnelles
        /// </summary>
        /// <param name="toEmail">Email du destinataire (parent)</param>
        /// <param name="subject">Sujet de l'email</param>
        /// <param name="body">Corps du message (texte brut)</param>
        /// <param name="attachmentPaths">Chemins des fichiers à joindre</param>
        /// <returns>Tuple (success, errorMessage)</returns>
        public async Task<(bool Success, string? Error)> SendEmailAsync(
            string toEmail,
            string subject,
            string body,
            List<string>? attachmentPaths = null)
        {
            if (!IsConfigured())
            {
                return (false, "Configuration SMTP incomplète. Veuillez configurer le mot de passe SMTP dans les Paramètres.");
            }

            if (string.IsNullOrWhiteSpace(toEmail))
            {
                return (false, "Adresse email du destinataire manquante.");
            }

            try
            {
                using var message = new MailMessage();

                // Expéditeur
                message.From = new MailAddress(_settings.SmtpFromEmail, _settings.SmtpFromName);

                // Destinataire
                message.To.Add(new MailAddress(toEmail));

                // Sujet et corps
                message.Subject = subject;
                message.Body = body;
                message.IsBodyHtml = false;

                // Ajouter les pièces jointes
                var attachments = new List<Attachment>();
                if (attachmentPaths != null)
                {
                    foreach (var path in attachmentPaths)
                    {
                        if (File.Exists(path))
                        {
                            var attachment = new Attachment(path);
                            attachments.Add(attachment);
                            message.Attachments.Add(attachment);
                            System.Diagnostics.Debug.WriteLine($"[PilotageEmail] 📎 Pièce jointe: {Path.GetFileName(path)}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[PilotageEmail] ⚠️ Fichier non trouvé: {path}");
                        }
                    }
                }

                // Configuration SMTP
                using var smtp = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
                {
                    Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword),
                    EnableSsl = true,
                    Timeout = 30000 // 30 secondes
                };

                System.Diagnostics.Debug.WriteLine($"[PilotageEmail] 📧 Envoi vers {toEmail}...");

                await smtp.SendMailAsync(message);

                // Libérer les attachments
                foreach (var att in attachments)
                {
                    att.Dispose();
                }

                System.Diagnostics.Debug.WriteLine($"[PilotageEmail] ✅ Email envoyé avec succès à {toEmail}");
                return (true, null);
            }
            catch (SmtpException ex)
            {
                var errorMsg = $"Erreur SMTP: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[PilotageEmail] ❌ {errorMsg}");
                return (false, errorMsg);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Erreur d'envoi: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[PilotageEmail] ❌ {errorMsg}");
                return (false, errorMsg);
            }
        }

        /// <summary>
        /// Génère un sujet d'email standard pour une réponse Parent'aile
        /// </summary>
        public string GenerateSubject(string childNickname)
        {
            return $"Réponse du cabinet - {childNickname}";
        }

        /// <summary>
        /// Génère une signature standard pour les emails
        /// </summary>
        public string GenerateSignature()
        {
            return $@"

--
{_settings.Medecin}
{_settings.Specialite}
{_settings.Adresse}
Tél: {_settings.Telephone}

Ce message est envoyé via Parent'aile, l'application de liaison parent-soignant.
Merci de ne pas répondre directement à cet email.";
        }

        /// <summary>
        /// Compose le corps complet de l'email avec signature
        /// </summary>
        public string ComposeEmailBody(string responseText, string childNickname)
        {
            return $@"Bonjour,

Concernant {childNickname}, voici ma réponse à votre message :

{responseText}
{GenerateSignature()}";
        }

        /// <summary>
        /// Teste la connexion SMTP (valide la configuration)
        /// </summary>
        public (bool Success, string? Error) TestConnection()
        {
            if (!IsConfigured())
            {
                return (false, "Configuration SMTP incomplète.");
            }

            try
            {
                // Vérifier que les paramètres sont valides
                if (string.IsNullOrWhiteSpace(_settings.SmtpHost))
                    return (false, "Hôte SMTP manquant");

                if (_settings.SmtpPort <= 0 || _settings.SmtpPort > 65535)
                    return (false, "Port SMTP invalide");

                System.Diagnostics.Debug.WriteLine($"[PilotageEmail] 🔌 Configuration SMTP validée");
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
