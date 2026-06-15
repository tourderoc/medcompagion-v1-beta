using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Restitutions
{
    public enum RestitutionType
    {
        PremierEntretien,        // Restitution 1er entretien (existant)
        DossierInitial           // Dossier de restitution complet (Bilan + Projet)
    }

    public enum RestitutionStatut
    {
        Brouillon,
        Validee
    }

    public abstract class RestitutionBase : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public abstract RestitutionType Type { get; }

        private int _version = 1;
        public int Version { get => _version; set => SetProperty(ref _version, value); }

        private string? _versionPrecedenteFichier;
        public string? VersionPrecedenteFichier { get => _versionPrecedenteFichier; set => SetProperty(ref _versionPrecedenteFichier, value); }

        private RestitutionStatut _statut = RestitutionStatut.Brouillon;
        public RestitutionStatut Statut { get => _statut; set => SetProperty(ref _statut, value); }

        private DateTime _dateCreation = DateTime.Now;
        public DateTime DateCreation { get => _dateCreation; set => SetProperty(ref _dateCreation, value); }

        private DateTime? _dateValidation;
        public DateTime? DateValidation { get => _dateValidation; set => SetProperty(ref _dateValidation, value); }

        private string _patientNomComplet = string.Empty;
        public string PatientNomComplet { get => _patientNomComplet; set => SetProperty(ref _patientNomComplet, value); }

        private string? _generatedPdfPath;
        public string? GeneratedPdfPath { get => _generatedPdfPath; set => SetProperty(ref _generatedPdfPath, value); }

        public ObservableCollection<RestitutionBloc> Blocs { get; } = new ObservableCollection<RestitutionBloc>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    public class ReformulationEntry
    {
        public DateTime Date { get; set; } = DateTime.Now;
        public string Consigne { get; set; } = string.Empty;
        public string ResultatBrut { get; set; } = string.Empty;
    }

    public class RestitutionBloc : INotifyPropertyChanged
    {
        public string Key { get; set; } = string.Empty;
        public string Titre { get; set; } = string.Empty;
        public int Ordre { get; set; }
        public string VoixCible { get; set; } = "clinique"; // "livre" | "clinique" | "mixte"

        private string _contenuPreremplit = string.Empty;
        public string ContenuPreremplit { get => _contenuPreremplit; set => SetProperty(ref _contenuPreremplit, value); }

        private string _contenuValide = string.Empty;
        public string ContenuValide { get => _contenuValide; set => SetProperty(ref _contenuValide, value); }

        private bool _isValidated;
        public bool IsValidated { get => _isValidated; set => SetProperty(ref _isValidated, value); }

        private bool _isIncludedInPdf = true;
        public bool IsIncludedInPdf { get => _isIncludedInPdf; set => SetProperty(ref _isIncludedInPdf, value); }

        private string? _sourceCliniqueFichier;
        public string? SourceCliniqueFichier { get => _sourceCliniqueFichier; set => SetProperty(ref _sourceCliniqueFichier, value); }

        public ObservableCollection<ReformulationEntry> Historique { get; } = new ObservableCollection<ReformulationEntry>();

        public RestitutionBloc(string key, string titre, int ordre, string voixCible)
        {
            Key = key;
            Titre = titre;
            Ordre = ordre;
            VoixCible = voixCible;
        }
        
        public RestitutionBloc() { } // For serialization

        public event PropertyChangedEventHandler? PropertyChanged;
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    public class RestitutionPremierEntretien : RestitutionBase
    {
        public override RestitutionType Type => RestitutionType.PremierEntretien;
    }

    public class DossierRestitutionInitial : RestitutionBase
    {
        public override RestitutionType Type => RestitutionType.DossierInitial;

        public DossierRestitutionInitial()
        {
            // Initialisation des 20 sections. Blocs 3-7 = page Patient & Contexte.
            // Blocs carto_s1..s8 = 1 bloc par sphère (génération/régénération indépendante).
            Blocs.Add(new RestitutionBloc("couverture",                 "Identité & couverture",         1, "mixte"));
            Blocs.Add(new RestitutionBloc("restitution_1page",          "Restitution 1-page parents",    2, "livre"));
            Blocs.Add(new RestitutionBloc("patient_identification",     "Identification",                3, "clinique"));
            Blocs.Add(new RestitutionBloc("patient_motif",              "Motif de consultation",         4, "clinique"));
            Blocs.Add(new RestitutionBloc("patient_contexte_familial",  "Contexte familial",             5, "clinique"));
            Blocs.Add(new RestitutionBloc("patient_antecedents",        "Antécédents",                   6, "clinique"));
            Blocs.Add(new RestitutionBloc("patient_situation_actuelle", "Situation actuelle",            7, "clinique"));
            Blocs.Add(new RestitutionBloc("carto_s1", "🐛 Sphère 1 — Attachement",               8,  "clinique"));
            Blocs.Add(new RestitutionBloc("carto_s2", "🐛 Sphère 2 — Régulation émotionnelle",   9,  "clinique"));
            Blocs.Add(new RestitutionBloc("carto_s3", "🐛 Sphère 3 — Langage",                  10,  "clinique"));
            Blocs.Add(new RestitutionBloc("carto_s4", "🐛 Sphère 4 — Tempérament",              11,  "clinique"));
            Blocs.Add(new RestitutionBloc("carto_s5", "🐛 Sphère 5 — Psychomotricité",          12,  "clinique"));
            Blocs.Add(new RestitutionBloc("carto_s6", "🐛 Sphère 6 — Imagination & Jeu",        13,  "clinique"));
            Blocs.Add(new RestitutionBloc("carto_s7", "🐛 Sphère 7 — Pensée & Apprentissages",  14,  "clinique"));
            Blocs.Add(new RestitutionBloc("carto_s8", "🐛 Sphère 8 — Attention & FE",           15,  "clinique"));
            Blocs.Add(new RestitutionBloc("env_edu_f1", "🍃 Environnement — Famille",            16, "clinique"));
            Blocs.Add(new RestitutionBloc("env_edu_f2", "🍃 Environnement — École & Pairs",      17, "clinique"));
            Blocs.Add(new RestitutionBloc("env_edu_f3", "🍃 Environnement — Écrans & Médias",    18, "clinique"));
            Blocs.Add(new RestitutionBloc("env_edu_f4", "🍃 Environnement — Valeurs Sociétales", 19, "clinique"));
            Blocs.Add(new RestitutionBloc("env_edu_f5", "🍃 Environnement — Cadre Éducatif",     20, "clinique"));
            Blocs.Add(new RestitutionBloc("env_edu_global", "🍃 Lecture globale Branche Éducative", 21, "clinique"));
            Blocs.Add(new RestitutionBloc("synthese_diag_s1", "🔬 Synthèse — Compréhension globale",    22, "clinique"));
            Blocs.Add(new RestitutionBloc("synthese_diag_s2", "🔬 Synthèse — Diagnostics retenus",       23, "clinique"));
            Blocs.Add(new RestitutionBloc("synthese_diag_s3", "🔬 Synthèse — Différentiels écartés",     24, "clinique"));
            Blocs.Add(new RestitutionBloc("synthese_diag_s4", "🔬 Synthèse — Intégration cartographies", 25, "clinique"));
            Blocs.Add(new RestitutionBloc("synthese_diag_s5", "🔬 Synthèse — Conclusion intégrative",    26, "clinique"));
            Blocs.Add(new RestitutionBloc("pt_s1", "💊 Projet — Prise en charge médicale",        27, "clinique"));
            Blocs.Add(new RestitutionBloc("pt_s2", "🧠 Projet — Accompagnement psychologique",     28, "clinique"));
            Blocs.Add(new RestitutionBloc("pt_s3", "🌱 Projet — Développement global",              29, "clinique"));
            Blocs.Add(new RestitutionBloc("pt_s4", "👨‍👩‍👧 Projet — Accompagnement parental & familial", 30, "clinique"));
            Blocs.Add(new RestitutionBloc("pt_s5", "🏫 Projet — École & développement",             31, "clinique"));
            Blocs.Add(new RestitutionBloc("conclusion", "Conclusion et perspectives",                32, "mixte"));
        }
    }
}
