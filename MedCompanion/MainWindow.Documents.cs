using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Dialogs;

namespace MedCompanion;

public partial class MainWindow : Window
{
    // ===== BLOC COURRIERS SUPPRIMÉ =====
    // Code migré vers Views/Courriers/CourriersControl.xaml.cs
    // Supprimé le 23/11/2025 après validation
    // Méthodes supprimées : TemplateLetterCombo_SelectionChanged, LettersList_SelectionChanged,
    // LettersList_MouseDoubleClick, ImprimerLetterButton_Click, LetterEditText_TextChanged,
    // ModifierLetterButton_Click, AnnulerLetterButton_Click, SupprimerLetterButton_Click,
    // SauvegarderLetterButton_Click, RefreshLettersList, LoadPatientLetters


    // ===== BLOC TEMPLATES SUPPRIMÉ =====
    // Code migré vers Views/Templates/TemplatesControl.xaml.cs
    // Supprimé le 05/12/2025 après migration MVVM
    // Méthodes supprimées : AnalyzeLetterBtn_Click, SaveTemplateBtn_Click, ChatInput_TextChanged
    // Note: ChatInput_TextChanged était déjà vide (détection automatique supprimée)





    // ===== BLOC ATTESTATIONS SUPPRIMÉ =====
    // Code migré vers Views/Attestations/AttestationsControl.xaml.cs
    // Supprimé le 23/11/2025 après validation

}
