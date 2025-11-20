using System;

namespace MedCompanion.Models;

public class PAISynthesis
{
    public string Type { get; set; } = "PAI";
    public DateTime DateCreation { get; set; }
    public string Patient { get; set; } = string.Empty;
    public string Motif { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}
