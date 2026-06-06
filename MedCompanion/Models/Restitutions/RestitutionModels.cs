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

    public class DossierRestitutionInitial : RestitutionBase
    {
        public override RestitutionType Type => RestitutionType.DossierInitial;

        public DossierRestitutionInitial()
        {
            // Initialisation des 8 sections principales
            Blocs.Add(new RestitutionBloc("couverture", "Identité & couverture", 1, "mixte"));
            Blocs.Add(new RestitutionBloc("restitution_1page", "Restitution 1-page parents", 2, "livre"));
            Blocs.Add(new RestitutionBloc("patient_contexte", "Patient & Contexte", 3, "clinique"));
            Blocs.Add(new RestitutionBloc("synthese_diag", "Synthèse diagnostique", 4, "clinique"));
            Blocs.Add(new RestitutionBloc("bilan_final", "Bilan final détaillé", 5, "clinique"));
            Blocs.Add(new RestitutionBloc("synthese_globale", "Synthèse globale", 6, "clinique"));
            Blocs.Add(new RestitutionBloc("projet_therapeutique", "Projet Thérapeutique Global", 7, "clinique"));
            Blocs.Add(new RestitutionBloc("conclusion", "Conclusion et perspectives", 8, "mixte"));
        }
    }
}
