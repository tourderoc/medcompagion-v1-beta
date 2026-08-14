using System;

namespace MedCompanion.Models
{
    public class FormulaireData
    {
        // Bloc 1 — Père
        public string PerePrenom  { get; set; } = "";
        public string PereNom     { get; set; } = "";
        public bool   PereNomMeme { get; set; } // Nom = celui de l'enfant (case cochée sur le papier)
        public string PereTel     { get; set; } = "";
        public string PereEmail   { get; set; } = "";

        // Bloc 2 — Mère
        public string MerePrenom  { get; set; } = "";
        public string MereNom     { get; set; } = "";
        public bool   MereNomMeme { get; set; } // Nom = celui de l'enfant (case cochée sur le papier)
        public string MereTel     { get; set; } = "";
        public string MereEmail   { get; set; } = "";

        // Bloc 3 — Adresse 1
        public string Adresse      { get; set; } = "";
        public string CodePostal   { get; set; } = "";
        public string Ville        { get; set; } = "";
        // "mere" | "pere" | "autre" | ""
        public string GardeAdresse1 { get; set; } = "";

        // Bloc 3 — Adresse 2 (garde alternée)
        public string Adresse2      { get; set; } = "";
        public string CodePostal2   { get; set; } = "";
        public string Ville2        { get; set; } = "";
        // "mere" | "pere" | "autre" | ""
        public string GardeAdresse2 { get; set; } = "";

        // Bloc 4 — Situation familiale
        // "ensemble" | "separes" | "divorces" | "garde_alternee" | "recomposee" | "autre"
        public string SituationFamiliale { get; set; } = "";
        // "parents" | "mere" | "pere" | "autre"
        public string GardePrincipale    { get; set; } = "";

        // Bloc 5 — Antécédents familiaux : "oui" | "non" | "nsp"
        public string Tdah             { get; set; } = "";
        public string Dyslexie         { get; set; } = "";
        public string Tsa              { get; set; } = "";
        public string TroublesAnxieux  { get; set; } = "";
        public string Depression       { get; set; } = "";
        public string Bipolarite       { get; set; } = "";
        public string Addictions       { get; set; } = "";
        public string TentativeSuicide { get; set; } = "";
        public string AntecedentsAutreLabel { get; set; } = "";
        public string AntecedentsAutre      { get; set; } = "";

        // Bloc 7 — Autorisations : "oui" | "non"
        public string AutorUsageInfos { get; set; } = ""; // Utiliser les infos du formulaire pour le suivi
        public string AutorSms        { get; set; } = ""; // Envoi de SMS
        public string AutorEmail      { get; set; } = ""; // Envoi d'emails

        public DateTime DateSaisie        { get; set; } = DateTime.Now;
        public string?  LinkedDocumentPath { get; set; }
    }
}
