using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services.Restitutions;
using MedCompanion.Models.Restitutions;

namespace MedCompanion.ViewModels.Restitutions
{
    public class RestitutionDocumentCardViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string DateText { get; set; } = string.Empty;
        public string PdfPath { get; set; } = string.Empty;
        public string MarkdownPath { get; set; } = string.Empty;

        public bool HasPdf => !string.IsNullOrEmpty(PdfPath);

        public ICommand OpenPdfCommand { get; }

        public RestitutionDocumentCardViewModel()
        {
            OpenPdfCommand = new RelayCommand(_ =>
            {
                if (!string.IsNullOrEmpty(PdfPath) && File.Exists(PdfPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = PdfPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Restitution] Error opening PDF: {ex.Message}");
                    }
                }
            });
        }
    }

    public class RestitutionsHubViewModel : INotifyPropertyChanged
    {
        private readonly RestitutionService _restitutionService;
        private PatientIndexEntry? _currentPatient;

        public ObservableCollection<RestitutionDocumentCardViewModel> Restitutions { get; } = new();

        public bool HasRestitutions => Restitutions.Count > 0;
        public bool HasNoRestitutions => !HasRestitutions;

        public ICommand CreateNewCommand { get; }

        public event Action? RequestCreateNew;

        public RestitutionsHubViewModel(RestitutionService restitutionService)
        {
            _restitutionService = restitutionService;
            CreateNewCommand = new RelayCommand(_ => RequestCreateNew?.Invoke());
        }

        public async Task LoadForPatientAsync(PatientIndexEntry patient)
        {
            _currentPatient = patient;
            Restitutions.Clear();

            var restitutions = await _restitutionService.ListRestitutionsAsync(patient.NomComplet);
            foreach (var r in restitutions)
            {
                var title = r.Type == RestitutionType.PremierEntretien ? "Restitution 1er entretien" : "Dossier Restitution Initial";
                var dateText = r.DateCreation.ToString("dd/MM/yyyy");
                var isDraft = r.Statut == RestitutionStatut.Brouillon;
                if (isDraft) title += " (Brouillon)";

                Restitutions.Add(new RestitutionDocumentCardViewModel
                {
                    Title = title,
                    DateText = dateText,
                    MarkdownPath = r.Id, // using Id which stores the path
                    PdfPath = r.GeneratedPdfPath ?? ""
                });
            }

            OnPropertyChanged(nameof(HasRestitutions));
            OnPropertyChanged(nameof(HasNoRestitutions));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
