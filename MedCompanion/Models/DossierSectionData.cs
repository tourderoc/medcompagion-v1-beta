using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models
{
    /// <summary>
    /// Contient toutes les pages d'une section (intercalaire) du dossier
    /// Gère la navigation entre les pages de la section
    /// </summary>
    public class DossierSectionData : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public DossierSectionData(DossierTab section)
        {
            Section = section;
            Pages = new ObservableCollection<DossierPageItem>();
            Pages.CollectionChanged += (s, e) => UpdatePageProperties();
        }

        private bool _isDoublePageMode = true;
        /// <summary>
        /// Mode double page (true) ou page simple (false)
        /// En mode double page (F3): affiche 2 pages côte à côte
        /// En mode simple (F2): affiche 1 page à la fois pour la lisibilité
        /// </summary>
        public bool IsDoublePageMode
        {
            get => _isDoublePageMode;
            set
            {
                if (SetProperty(ref _isDoublePageMode, value))
                {
                    UpdatePageProperties();
                }
            }
        }

        /// <summary>
        /// Section (intercalaire) représentée
        /// </summary>
        public DossierTab Section { get; }

        /// <summary>
        /// Collection de toutes les pages de cette section
        /// </summary>
        public ObservableCollection<DossierPageItem> Pages { get; }

        private int _currentPageIndex = 0;
        /// <summary>
        /// Index de la page actuellement affichée à gauche (0-based)
        /// En mode double-page, affiche CurrentPageIndex et CurrentPageIndex+1
        /// En mode simple, affiche uniquement CurrentPageIndex
        /// </summary>
        public int CurrentPageIndex
        {
            get => _currentPageIndex;
            set
            {
                // S'assurer que l'index est valide
                var newValue = Math.Max(0, Math.Min(value, Math.Max(0, Pages.Count - 1)));

                // En mode double page, arrondir à l'index pair inférieur
                if (IsDoublePageMode)
                {
                    newValue = (newValue / 2) * 2;
                }

                if (SetProperty(ref _currentPageIndex, newValue))
                {
                    UpdatePageProperties();
                }
            }
        }

        /// <summary>
        /// Nombre total de pages dans cette section
        /// </summary>
        public int TotalPages => Pages.Count;

        /// <summary>
        /// Indique s'il y a des pages précédentes
        /// </summary>
        public bool HasPreviousPage => CurrentPageIndex > 0;

        /// <summary>
        /// Indique s'il y a des pages suivantes
        /// En mode double page: vérifie s'il reste 2 pages après
        /// En mode simple: vérifie s'il reste 1 page après
        /// </summary>
        public bool HasNextPage => IsDoublePageMode
            ? CurrentPageIndex + 2 < Pages.Count
            : CurrentPageIndex + 1 < Pages.Count;

        /// <summary>
        /// Page affichée à gauche (peut être null si section vide)
        /// </summary>
        public DossierPageItem? LeftPage => Pages.ElementAtOrDefault(CurrentPageIndex);

        /// <summary>
        /// Page affichée à droite (peut être null si une seule page)
        /// </summary>
        public DossierPageItem? RightPage => Pages.ElementAtOrDefault(CurrentPageIndex + 1);

        /// <summary>
        /// Indique si la section est vide
        /// </summary>
        public bool IsEmpty => Pages.Count == 0;

        /// <summary>
        /// Indique si la section a une seule page (pas de navigation possible)
        /// </summary>
        public bool HasSinglePage => Pages.Count == 1;

        /// <summary>
        /// Texte indicateur de page
        /// Mode double: "Pages 1-2 / 6"
        /// Mode simple: "Page 1 / 6"
        /// </summary>
        public string PageIndicator
        {
            get
            {
                if (IsEmpty) return "Aucune page";
                if (HasSinglePage) return "Page 1 / 1";

                if (IsDoublePageMode)
                {
                    var leftNum = CurrentPageIndex + 1;
                    var rightNum = Math.Min(CurrentPageIndex + 2, TotalPages);
                    return $"Pages {leftNum}-{rightNum} / {TotalPages}";
                }
                else
                {
                    return $"Page {CurrentPageIndex + 1} / {TotalPages}";
                }
            }
        }

        /// <summary>
        /// Nom de la section pour l'affichage
        /// </summary>
        public string SectionName => Section switch
        {
            DossierTab.Couverture => "Couverture",
            DossierTab.Synthese => "Synthèse",
            DossierTab.Administratif => "Administratif",
            DossierTab.Consultations => "Consultations",
            DossierTab.ProjetTherapeutique => "Projet Thérapeutique",
            DossierTab.Bilans => "Bilans",
            DossierTab.Documents => "Documents",
            _ => Section.ToString()
        };

        /// <summary>
        /// Navigue vers les pages suivantes
        /// En mode double: avance de 2 pages
        /// En mode simple: avance de 1 page
        /// </summary>
        public void GoToNextPage()
        {
            if (HasNextPage)
            {
                CurrentPageIndex += IsDoublePageMode ? 2 : 1;
            }
        }

        /// <summary>
        /// Navigue vers les pages précédentes
        /// En mode double: recule de 2 pages
        /// En mode simple: recule de 1 page
        /// </summary>
        public void GoToPreviousPage()
        {
            if (HasPreviousPage)
            {
                CurrentPageIndex -= IsDoublePageMode ? 2 : 1;
            }
        }

        /// <summary>
        /// Revient à la première page
        /// </summary>
        public void GoToFirstPage()
        {
            CurrentPageIndex = 0;
        }

        /// <summary>
        /// Va à la dernière page
        /// </summary>
        public void GoToLastPage()
        {
            if (Pages.Count > 0)
            {
                if (IsDoublePageMode)
                {
                    // Arrondir à la paire contenant la dernière page
                    CurrentPageIndex = ((Pages.Count - 1) / 2) * 2;
                }
                else
                {
                    CurrentPageIndex = Pages.Count - 1;
                }
            }
        }

        /// <summary>
        /// Met à jour les propriétés liées aux pages
        /// </summary>
        private void UpdatePageProperties()
        {
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(HasPreviousPage));
            OnPropertyChanged(nameof(HasNextPage));
            OnPropertyChanged(nameof(LeftPage));
            OnPropertyChanged(nameof(RightPage));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasSinglePage));
            OnPropertyChanged(nameof(PageIndicator));
            OnPropertyChanged(nameof(IsDoublePageMode));
        }
    }
}
