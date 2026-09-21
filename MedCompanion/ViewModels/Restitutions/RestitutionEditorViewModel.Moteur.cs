using System;
using System.Collections.Generic;
using System.Linq;
using MedCompanion.Services.LLM;

namespace MedCompanion.ViewModels.Restitutions
{
    /// <summary>
    /// Choix du moteur depuis l'éditeur de restitution, sans passer par le panneau Pilotage.
    ///
    /// TROIS ENTRÉES POUR DEUX MODÈLES. Le troisième choix n'est pas un troisième fichier : il
    /// n'y a que deux GGUF sur le SSD (Qwen 13,5 Go, Gemma QAT 6,7 Go — le Gemma standard a été
    /// retiré le 14/09/2026). Ce qui change entre les deux entrées Gemma, c'est la
    /// <b>quantification du cache de contexte</b> : <c>-ctk q8_0 -ctv q8_0</c> ou rien. Mêmes
    /// poids, même brouillon MTP, seul le cache KV diffère.
    ///
    /// LE SERVEUR EST ARRÊTÉ AU CHANGEMENT, ET C'EST INDISPENSABLE. Les arguments de
    /// llama-server sont fixés à son démarrage : changer <c>KvQuantized</c> sur un serveur qui
    /// tourne ne fait rien — le panneau Pilotage le dit franchement (« actif après redémarrage »).
    /// Ici on ne peut pas se le permettre : le médecin choisit ce réglage POUR comparer. S'il
    /// sélectionnait « contexte non quantifié » et générait avec l'ancien serveur, il conclurait
    /// sur le mauvais. On arrête donc le serveur ; la prochaine génération le relance avec les
    /// bons arguments, au prix d'un chargement (4,6 à 5,5 s mesurés le 15/09).
    /// </summary>
    public partial class RestitutionEditorViewModel
    {
        public const string MoteurQwen           = "Qwen 3.8-27B";
        public const string MoteurGemmaQuantifie = "Gemma 4 QAT — contexte q8_0";
        public const string MoteurGemmaPlein     = "Gemma 4 QAT — contexte non quantifié";

        public IReadOnlyList<string> MoteursGeneration { get; } = new[]
        {
            MoteurQwen, MoteurGemmaQuantifie, MoteurGemmaPlein
        };

        private string? _moteurGeneration;

        /// <summary>
        /// Moteur servant à générer les blocs du dossier. Lu depuis l'état réel du moteur, pas
        /// depuis une copie : si le modèle a été changé ailleurs dans Med, le sélecteur le dit.
        /// </summary>
        public string MoteurGeneration
        {
            get => _moteurGeneration ??= LireMoteurCourant();
            set
            {
                if (string.IsNullOrWhiteSpace(value) || _moteurGeneration == value) return;
                _moteurGeneration = value;
                AppliquerMoteur(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(MoteurGenerationNote));
            }
        }

        private string _moteurGenerationNote = "";

        /// <summary>Ce qui vient de se passer, dit en clair — un réglage silencieux se croit sans effet.</summary>
        public string MoteurGenerationNote
        {
            get => _moteurGenerationNote;
            private set { if (_moteurGenerationNote == value) return; _moteurGenerationNote = value; OnPropertyChanged(); }
        }

        private static string LireMoteurCourant()
        {
            try
            {
                var p = LlamaCppServerManager.CurrentProfile;
                if (p == null) return MoteurQwen;
                if (p.Id == LlamaCppProfiles.Qwen.Id) return MoteurQwen;
                return p.KvQuantized ? MoteurGemmaQuantifie : MoteurGemmaPlein;
            }
            catch { return MoteurQwen; }
        }

        private void AppliquerMoteur(string choix)
        {
            try
            {
                var versQwen = choix == MoteurQwen;
                var profil   = versQwen ? LlamaCppProfiles.Qwen : LlamaCppProfiles.Gemma4Qat;

                // Le cache KV n'est réglé que sur le profil concerné : basculer sur Qwen ne doit
                // pas modifier au passage la configuration de Gemma.
                if (!versQwen) profil.KvQuantized = choix == MoteurGemmaQuantifie;

                LlamaCppServerManager.CurrentProfile = profil;
                LlamaCppProfiles.SaveSettings();

                // Sans cet arrêt, le réglage du cache ne prendrait effet qu'au prochain
                // redémarrage fortuit — et la comparaison porterait sur le mauvais serveur.
                var tournait = LlamaCppServerManager.IsRunning;
                if (tournait) LlamaCppServerManager.Stop();

                MoteurGenerationNote = tournait
                    ? "Serveur arrêté — il se relance à la prochaine génération (quelques secondes)."
                    : "Enregistré — actif dès la prochaine génération.";
            }
            catch (Exception ex)
            {
                MoteurGenerationNote = $"Changement de moteur impossible : {ex.Message}";
            }
        }

        /// <summary>
        /// Résout le moteur à utiliser pour un bloc : son moteur spécifique s'il en a un,
        /// sinon le moteur global du dossier.
        /// </summary>
        public string ResoudreMoteurEffectif(RestitutionBlocViewModel bloc)
        {
            if (!string.IsNullOrWhiteSpace(bloc.Model.MoteurCible))
                return bloc.Model.MoteurCible;
            return MoteurGeneration;
        }

        /// <summary>
        /// S'assure que le moteur voulu est bien celui qui tourne. Si le moteur ou la quantification
        /// KV diffère, applique le réglage et relance le serveur immédiatement (~4.8s grâce à la
        /// prélecture en RAM).
        /// </summary>
        public async System.Threading.Tasks.Task BasculerMoteurSiNecessaireAsync(string choix)
        {
            if (string.IsNullOrWhiteSpace(choix)) return;

            var courant = LireMoteurCourant();
            bool tourne = LlamaCppServerManager.IsRunning;

            if (tourne && courant == choix)
                return; // Le bon modèle tourne déjà, zéro latence

            StatusMessage = $"🔄 Bascule du moteur vers {choix}...";
            AppliquerMoteur(choix);
            var (ok, msg) = await LlamaCppServerManager.EnsureRunningAsync();
            if (!ok)
            {
                StatusMessage = $"⚠ Erreur démarrage {choix} : {msg}";
            }
        }
    }
}
