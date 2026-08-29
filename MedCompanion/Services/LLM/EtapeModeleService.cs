using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services.LLM
{
    /// <summary>Modèle affecté à une étape.</summary>
    public class AffectationEtape
    {
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public bool EstDefinie => !string.IsNullOrWhiteSpace(Provider) && !string.IsNullOrWhiteSpace(Model);
    }

    /// <summary>
    /// Affecte un modèle à chaque étape de consultation et bascule automatiquement au moment de
    /// l'exécuter.
    ///
    /// CE QUE ÇA COÛTE, ET POURQUOI ON LE COMPTE — Qwen (12,6 Go) et Gemma (9 Go) ne tiennent pas
    /// ensemble dans 16 Go de VRAM : changer de modèle arrête et relance llama-server, soit 6 à 10
    /// secondes mesurées. Une bascule au mauvais moment, c'est une attente devant une famille. D'où
    /// <see cref="CompterBascules"/> : l'interface doit montrer combien de fois le parcours configuré
    /// fera payer ce prix, pour que le regroupement des étapes par modèle soit une décision et non
    /// un hasard.
    ///
    /// Les étapes marquées <see cref="EtapeConsultation.EnArrierePlan"/> n'ont volontairement pas
    /// d'affectation : elles héritent du modèle courant.
    /// </summary>
    public class EtapeModeleService
    {
        private readonly LLMServiceFactory _factory;
        private readonly string _fichier;

        private Dictionary<string, AffectationEtape> _affectations = new();

        /// <summary>
        /// Sérialise les bascules : deux étapes déclenchées presque en même temps demanderaient
        /// deux redémarrages concurrents de llama-server, et c'est ainsi qu'on se retrouve avec deux
        /// modèles en VRAM et la machine figée.
        /// </summary>
        private readonly SemaphoreSlim _verrou = new(1, 1);

        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public EtapeModeleService(LLMServiceFactory factory)
        {
            _factory = factory;

            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MedCompanion");
            Directory.CreateDirectory(appData);
            _fichier = Path.Combine(appData, "etapes-modeles.json");

            Charger();
        }

        /// <summary>Bascule automatique active. À false, tout reste sur le modèle choisi à la main.</summary>
        public bool Actif { get; private set; } = true;

        public void DefinirActif(bool actif) { Actif = actif; Sauver(); }

        // ── Affectations ──────────────────────────────────────────────────────

        public AffectationEtape? Affectation(string etapeId)
            => _affectations.TryGetValue(etapeId, out var a) && a.EstDefinie ? a : null;

        /// <summary>
        /// Affecte un modèle à une étape. Sans effet sur une étape d'arrière-plan : lui donner un
        /// modèle propre provoquerait un redémarrage de serveur pendant que le médecin fait autre
        /// chose (voir <see cref="EtapeConsultation.EnArrierePlan"/>).
        /// </summary>
        public void Definir(string etapeId, string provider, string model)
        {
            var etape = EtapesConsultation.Par(etapeId);
            if (etape == null || etape.EnArrierePlan) return;

            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
                _affectations.Remove(etapeId);
            else
                _affectations[etapeId] = new AffectationEtape { Provider = provider, Model = model };

            Sauver();
        }

        public void Effacer(string etapeId)
        {
            if (_affectations.Remove(etapeId)) Sauver();
        }

        // ── Bascule ───────────────────────────────────────────────────────────

        /// <summary>
        /// Met le moteur sur le modèle prévu pour l'étape, si besoin. À appeler juste AVANT l'appel
        /// LLM de l'étape.
        ///
        /// Ne fait rien — et c'est voulu — quand l'étape n'a pas d'affectation, quand le modèle est
        /// déjà le bon, ou quand la bascule automatique est désactivée. Le cas « déjà le bon » est
        /// le plus fréquent et doit rester gratuit : le tester ici évite un redémarrage inutile de
        /// plusieurs secondes à chaque étape d'un parcours homogène.
        /// </summary>
        public async Task<(bool bascule, string? message)> AssurerModeleAsync(string etapeId)
        {
            if (!Actif) return (false, null);

            var cible = Affectation(etapeId);
            if (cible == null) return (false, null);

            await _verrou.WaitAsync().ConfigureAwait(false);
            try
            {
                // Relu SOUS le verrou : une autre étape a pu basculer pendant l'attente, auquel cas
                // le modèle est peut-être déjà celui qu'on voulait.
                if (_factory.GetActiveProviderName() == cible.Provider &&
                    string.Equals(_factory.GetActiveModelName(), cible.Model, StringComparison.OrdinalIgnoreCase))
                    return (false, null);

                var (ok, message) = await _factory.SwitchProviderAsync(cible.Provider, cible.Model)
                                                  .ConfigureAwait(false);

                // Échec de bascule : on NE bloque PAS l'étape. Mieux vaut une extraction faite avec
                // le modèle en place qu'une étape annulée en pleine consultation.
                return (ok, ok ? message : $"Bascule vers {cible.Model} impossible ({message}) — poursuite avec le modèle courant.");
            }
            finally { _verrou.Release(); }
        }

        // ── Lecture pour le schéma ────────────────────────────────────────────

        /// <summary>
        /// Nombre de changements de modèle qu'entraînera le parcours d'une phase, dans l'ordre des
        /// étapes. Les étapes d'arrière-plan et les étapes non affectées sont transparentes : elles
        /// n'imposent aucun changement.
        /// </summary>
        public int CompterBascules(string phase)
        {
            string? precedent = null;
            var bascules = 0;

            foreach (var etape in EtapesConsultation.Toutes.Where(e => e.Phase == phase && !e.EnArrierePlan))
            {
                var a = Affectation(etape.Id);
                if (a == null) continue;

                var cle = a.Provider + "|" + a.Model;
                if (precedent != null && cle != precedent) bascules++;
                precedent = cle;
            }
            return bascules;
        }

        /// <summary>Estimation du temps perdu en bascules, en secondes. 8 s = milieu du 6-10 mesuré.</summary>
        public int SecondesEstimees(string phase) => CompterBascules(phase) * 8;

        // ── Persistance ───────────────────────────────────────────────────────

        private void Charger()
        {
            try
            {
                if (!File.Exists(_fichier)) return;
                var d = JsonSerializer.Deserialize<Fichier>(File.ReadAllText(_fichier), _opts);
                if (d == null) return;

                Actif = d.Actif;

                // Filtré à la lecture : le catalogue d'étapes peut changer d'une version à l'autre,
                // et une affectation orpheline resterait invisible tout en faussant les compteurs.
                _affectations = d.Affectations
                    .Where(kv => EtapesConsultation.Par(kv.Key) is { EnArrierePlan: false })
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }
            catch { /* réglages illisibles : catalogue vide, comportement d'avant */ }
        }

        private void Sauver()
        {
            try
            {
                File.WriteAllText(_fichier, JsonSerializer.Serialize(
                    new Fichier { Actif = Actif, Affectations = _affectations }, _opts));
            }
            catch { /* la persistance ne doit jamais bloquer un réglage appliqué en mémoire */ }
        }

        private class Fichier
        {
            public bool Actif { get; set; } = true;
            public Dictionary<string, AffectationEtape> Affectations { get; set; } = new();
        }
    }
}
