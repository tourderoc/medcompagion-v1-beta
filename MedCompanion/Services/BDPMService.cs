using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MedCompanion.Models;
using Microsoft.Data.Sqlite;

namespace MedCompanion.Services;

/// <summary>
/// Service pour gérer la base BDPM (Base de Données Publique des Médicaments)
/// Téléchargement, parsing et recherche
/// </summary>
public class BDPMService
{
    private readonly string _bdpmDirectory;
    private readonly string _databasePath;

    // URLs directes vers les fichiers BDPM (data.gouv.fr - source officielle)
    private const string CIS_URL = "https://www.data.gouv.fr/fr/datasets/r/9c85ddfc-e3f4-4b90-bf1f-c5c7bb28ac74";
    private const string CIP_URL = "https://www.data.gouv.fr/fr/datasets/r/d68e1512-f54d-4504-ab06-1f8de59dab53";
    private const string COMPO_URL = "https://www.data.gouv.fr/fr/datasets/r/5a2fb946-1dd3-4d32-8299-4cdc46887c0c";

    public BDPMService()
    {
        // Dossier de stockage : Documents/MedCompanion/bdpm/
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _bdpmDirectory = Path.Combine(documentsPath, "MedCompanion", "bdpm");
        _databasePath = Path.Combine(_bdpmDirectory, "medicaments.db");

        // Créer le dossier s'il n'existe pas
        if (!Directory.Exists(_bdpmDirectory))
        {
            Directory.CreateDirectory(_bdpmDirectory);
            Debug.WriteLine($"[BDPMService] Dossier créé: {_bdpmDirectory}");
        }
    }

    /// <summary>
    /// Vérifie si les fichiers BDPM sont présents dans le dossier
    /// Si oui, retourne succès. Sinon, affiche les instructions pour téléchargement manuel.
    /// </summary>
    public Task<(bool success, string message)> DownloadBDPMAsync()
    {
        try
        {
            Debug.WriteLine("[BDPMService] Vérification des fichiers BDPM...");

            // Liste des fichiers requis
            var requiredFiles = new[]
            {
                "CIS_bdpm.txt",           // Spécialités
                "CIS_CIP_bdpm.txt",       // Présentations
                "CIS_COMPO_bdpm.txt"      // Compositions
            };

            var missingFiles = new List<string>();
            var existingFiles = new List<string>();

            foreach (var fileName in requiredFiles)
            {
                var filePath = Path.Combine(_bdpmDirectory, fileName);
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    existingFiles.Add($"✅ {fileName} ({fileInfo.Length / 1024} KB)");
                    Debug.WriteLine($"[BDPMService] ✅ Fichier trouvé: {fileName}");
                }
                else
                {
                    missingFiles.Add(fileName);
                    Debug.WriteLine($"[BDPMService] ❌ Fichier manquant: {fileName}");
                }
            }

            // Si tous les fichiers sont présents
            if (missingFiles.Count == 0)
            {
                Debug.WriteLine("[BDPMService] ✅ Tous les fichiers BDPM sont présents");
                return Task.FromResult((true, "✅ Fichiers BDPM trouvés :\n\n" + string.Join("\n", existingFiles)));
            }

            // Si des fichiers manquent, afficher les instructions
            var instructions = $@"❌ Fichiers BDPM manquants : {missingFiles.Count}/{requiredFiles.Length}

📥 TÉLÉCHARGEMENT MANUEL REQUIS :

1. Rendez-vous sur le site ANSM :
   https://base-donnees-publique.medicaments.gouv.fr/telechargement

2. Téléchargez les fichiers suivants :
   {string.Join("\n   ", missingFiles.Select(f => $"• {f}"))}

3. Placez-les dans le dossier :
   {_bdpmDirectory}

4. Cliquez à nouveau sur ce bouton pour vérifier.

📁 Fichiers déjà présents :
{(existingFiles.Count > 0 ? string.Join("\n", existingFiles) : "   Aucun")}";

            Debug.WriteLine($"[BDPMService] Instructions affichées pour téléchargement manuel");
            return Task.FromResult((false, instructions));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BDPMService] ❌ Erreur: {ex.Message}");
            return Task.FromResult((false, $"Erreur lors de la vérification des fichiers: {ex.Message}"));
        }
    }

    /// <summary>
    /// Parse les fichiers BDPM et les insère dans la base SQLite
    /// </summary>
    public async Task<(bool success, string message, int count)> ParseAndImportAsync()
    {
        try
        {
            Debug.WriteLine("[BDPMService] Début du parsing BDPM...");

            // Vérifier que les fichiers existent
            var cisFile = Path.Combine(_bdpmDirectory, "CIS_bdpm.txt");
            var cipFile = Path.Combine(_bdpmDirectory, "CIS_CIP_bdpm.txt");
            var compoFile = Path.Combine(_bdpmDirectory, "CIS_COMPO_bdpm.txt");

            if (!File.Exists(cisFile) || !File.Exists(cipFile) || !File.Exists(compoFile))
            {
                return (false, "❌ Fichiers BDPM manquants. Veuillez d'abord télécharger la base.", 0);
            }

            // Créer/recréer la base SQLite
            await CreateDatabaseAsync();

            // Parser et insérer les données
            var medicaments = await Task.Run(() => ParseCISFile(cisFile));
            Debug.WriteLine($"[BDPMService] {medicaments.Count} médicaments parsés");

            var presentations = await Task.Run(() => ParseCIPFile(cipFile));
            Debug.WriteLine($"[BDPMService] {presentations.Count} présentations parsées");

            var compositions = await Task.Run(() => ParseCompoFile(compoFile));
            Debug.WriteLine($"[BDPMService] {compositions.Count} compositions parsées");

            // Insérer dans SQLite
            await InsertMedicamentsAsync(medicaments, presentations, compositions);

            Debug.WriteLine("[BDPMService] ✅ Import BDPM terminé");
            return (true, $"✅ {medicaments.Count} médicaments importés avec succès", medicaments.Count);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BDPMService] ❌ Erreur parsing: {ex.Message}\n{ex.StackTrace}");
            return (false, $"Erreur lors du parsing: {ex.Message}", 0);
        }
    }

    /// <summary>
    /// Crée la base de données SQLite avec les tables nécessaires
    /// </summary>
    private async Task CreateDatabaseAsync()
    {
        // Supprimer l'ancienne base si elle existe
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
            Debug.WriteLine("[BDPMService] Ancienne base supprimée");
        }

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        var createTablesSql = @"
            CREATE TABLE Medicaments (
                CIS TEXT PRIMARY KEY,
                Denomination TEXT NOT NULL,
                Forme TEXT,
                VoieAdministration TEXT,
                StatutAMM TEXT,
                TypeProcedure TEXT,
                Commercialisation TEXT,
                DateAMM TEXT,
                StatutBDM TEXT,
                NumAutorisationEU TEXT,
                Titulaires TEXT,
                SurveillanceRenforcee TEXT,
                SearchText TEXT
            );

            CREATE TABLE Presentations (
                CIP13 TEXT PRIMARY KEY,
                CIS TEXT NOT NULL,
                CIP7 TEXT,
                Libelle TEXT,
                StatutAdministratif TEXT,
                EtatCommercialisation TEXT,
                DateDeclaration TEXT
            );

            CREATE TABLE Compositions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CIS TEXT NOT NULL,
                DesignationElement TEXT,
                CodeSubstance TEXT,
                DenominationSubstance TEXT,
                Dosage TEXT,
                ReferenceDosage TEXT,
                NatureComposant TEXT,
                NumeroLiaison TEXT
            );

            CREATE INDEX idx_medicaments_search ON Medicaments(SearchText);
            CREATE INDEX idx_medicaments_denom ON Medicaments(Denomination);
            CREATE INDEX idx_presentations_cis ON Presentations(CIS);
            CREATE INDEX idx_compositions_cis ON Compositions(CIS);
            CREATE INDEX idx_compositions_dci ON Compositions(DenominationSubstance);
        ";

        using var command = connection.CreateCommand();
        command.CommandText = createTablesSql;
        await command.ExecuteNonQueryAsync();

        Debug.WriteLine("[BDPMService] Tables SQLite créées");
    }

    /// <summary>
    /// Parse le fichier CIS_bdpm.txt (spécialités)
    /// Format: CIS\tDénomination\tForme\tVoies\tStatutAMM\tType\tComm\tDateAMM\tStatutBDM\tNumEU\tTitulaires\tSurveillance
    /// </summary>
    private List<Medicament> ParseCISFile(string filePath)
    {
        var medicaments = new List<Medicament>();

        // Le fichier BDPM peut utiliser soit TAB (\t) soit le caractère ^I (0x09 visible)
        // Essayer d'abord UTF-8, puis ISO-8859-1 en fallback
        Encoding encoding;
        try
        {
            encoding = Encoding.UTF8;
            var testLines = File.ReadLines(filePath, encoding).Take(1).ToList();
            if (testLines.Count == 0 || !testLines[0].Contains("^I"))
            {
                encoding = Encoding.GetEncoding("ISO-8859-1");
            }
        }
        catch
        {
            encoding = Encoding.GetEncoding("ISO-8859-1");
        }

        var lines = File.ReadAllLines(filePath, encoding);
        Debug.WriteLine($"[BDPMService] Lecture CIS: {lines.Length} lignes, encodage: {encoding.EncodingName}");

        foreach (var line in lines.Skip(0)) // Pas de header dans BDPM
        {
            try
            {
                // Le séparateur peut être \t ou ^I (caractère de contrôle visible)
                var cleanLine = line.Replace("^M", "").Replace("^I", "\t");
                var parts = cleanLine.Split('\t');

                if (parts.Length < 12)
                {
                    Debug.WriteLine($"[BDPMService] Ligne ignorée (trop courte): {parts.Length} colonnes");
                    continue;
                }

                var medicament = new Medicament
                {
                    CIS = parts[0].Trim(),
                    Denomination = parts[1].Trim(),
                    Forme = parts[2].Trim(),
                    VoieAdministration = parts[3].Trim(),
                    StatutAMM = parts[4].Trim(),
                    TypeProcedure = parts[5].Trim(),
                    Commercialisation = parts[6].Trim(),
                    DateAMM = parts[7].Trim(),
                    StatutBDM = parts[8].Trim(),
                    NumAutorisationEU = parts[9].Trim(),
                    Titulaires = parts[10].Trim(),
                    SurveillanceRenforcee = parts[11].Trim()
                };

                medicaments.Add(medicament);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BDPMService] Erreur parsing CIS ligne: {ex.Message}");
            }
        }

        return medicaments;
    }

    /// <summary>
    /// Parse le fichier CIS_CIP_bdpm.txt (présentations)
    /// Format: CIS\tCIP7\tLibelle\tStatutAdmin\tEtatComm\tDateDecl\tCIP13\tAgrement
    /// </summary>
    private List<MedicamentPresentation> ParseCIPFile(string filePath)
    {
        var presentations = new List<MedicamentPresentation>();

        // Détection automatique de l'encodage
        Encoding encoding;
        try
        {
            encoding = Encoding.UTF8;
            var testLines = File.ReadLines(filePath, encoding).Take(1).ToList();
            if (testLines.Count == 0 || !testLines[0].Contains("^I"))
            {
                encoding = Encoding.GetEncoding("ISO-8859-1");
            }
        }
        catch
        {
            encoding = Encoding.GetEncoding("ISO-8859-1");
        }

        var lines = File.ReadAllLines(filePath, encoding);
        Debug.WriteLine($"[BDPMService] Lecture CIP: {lines.Length} lignes, encodage: {encoding.EncodingName}");

        foreach (var line in lines.Skip(0))
        {
            try
            {
                var cleanLine = line.Replace("^M", "").Replace("^I", "\t");
                var parts = cleanLine.Split('\t');
                if (parts.Length < 7)
                {
                    Debug.WriteLine($"[BDPMService] Ligne CIP ignorée: {parts.Length} colonnes");
                    continue;
                }

                var presentation = new MedicamentPresentation
                {
                    CIS = parts[0].Trim(),
                    CIP7 = parts[1].Trim(),
                    Libelle = parts[2].Trim(),
                    StatutAdministratif = parts[3].Trim(),
                    EtatCommercialisation = parts[4].Trim(),
                    DateDeclaration = parts[5].Trim(),
                    CIP13 = parts[6].Trim()
                };

                presentations.Add(presentation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BDPMService] Erreur parsing CIP ligne: {ex.Message}");
            }
        }

        return presentations;
    }

    /// <summary>
    /// Parse le fichier CIS_COMPO_bdpm.txt (compositions)
    /// Format: CIS\tDesignElement\tCodeSubstance\tDenomSubstance\tDosage\tRefDosage\tNatureCompo\tNumLiaison
    /// </summary>
    private List<MedicamentComposition> ParseCompoFile(string filePath)
    {
        var compositions = new List<MedicamentComposition>();

        // Détection automatique de l'encodage
        Encoding encoding;
        try
        {
            encoding = Encoding.UTF8;
            var testLines = File.ReadLines(filePath, encoding).Take(1).ToList();
            if (testLines.Count == 0 || !testLines[0].Contains("^I"))
            {
                encoding = Encoding.GetEncoding("ISO-8859-1");
            }
        }
        catch
        {
            encoding = Encoding.GetEncoding("ISO-8859-1");
        }

        var lines = File.ReadAllLines(filePath, encoding);
        Debug.WriteLine($"[BDPMService] Lecture COMPO: {lines.Length} lignes, encodage: {encoding.EncodingName}");

        foreach (var line in lines.Skip(0))
        {
            try
            {
                var cleanLine = line.Replace("^M", "").Replace("^I", "\t");
                var parts = cleanLine.Split('\t');
                if (parts.Length < 8)
                {
                    Debug.WriteLine($"[BDPMService] Ligne COMPO ignorée: {parts.Length} colonnes");
                    continue;
                }

                var composition = new MedicamentComposition
                {
                    CIS = parts[0].Trim(),
                    DesignationElement = parts[1].Trim(),
                    CodeSubstance = parts[2].Trim(),
                    DenominationSubstance = parts[3].Trim(),
                    Dosage = parts[4].Trim(),
                    ReferenceDosage = parts[5].Trim(),
                    NatureComposant = parts[6].Trim(),
                    NumeroLiaison = parts[7].Trim()
                };

                compositions.Add(composition);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BDPMService] Erreur parsing COMPO ligne: {ex.Message}");
            }
        }

        return compositions;
    }

    /// <summary>
    /// Insère les médicaments dans la base SQLite
    /// </summary>
    private async Task InsertMedicamentsAsync(
        List<Medicament> medicaments,
        List<MedicamentPresentation> presentations,
        List<MedicamentComposition> compositions)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        try
        {
            // Insérer les médicaments
            foreach (var med in medicaments)
            {
                var searchText = $"{med.Denomination} {med.Forme}".ToLowerInvariant()
                    .Replace("é", "e").Replace("è", "e").Replace("ê", "e")
                    .Replace("à", "a").Replace("â", "a")
                    .Replace("ô", "o").Replace("ù", "u").Replace("û", "u")
                    .Replace("ï", "i").Replace("î", "i")
                    .Replace("ç", "c");

                var sql = @"INSERT INTO Medicaments
                    (CIS, Denomination, Forme, VoieAdministration, StatutAMM, TypeProcedure,
                     Commercialisation, DateAMM, StatutBDM, NumAutorisationEU, Titulaires, SurveillanceRenforcee, SearchText)
                    VALUES (@CIS, @Denom, @Forme, @Voie, @Statut, @Type, @Comm, @Date, @BDM, @EU, @Tit, @Surv, @Search)";

                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@CIS", med.CIS);
                cmd.Parameters.AddWithValue("@Denom", med.Denomination);
                cmd.Parameters.AddWithValue("@Forme", med.Forme);
                cmd.Parameters.AddWithValue("@Voie", med.VoieAdministration);
                cmd.Parameters.AddWithValue("@Statut", med.StatutAMM);
                cmd.Parameters.AddWithValue("@Type", med.TypeProcedure);
                cmd.Parameters.AddWithValue("@Comm", med.Commercialisation);
                cmd.Parameters.AddWithValue("@Date", med.DateAMM);
                cmd.Parameters.AddWithValue("@BDM", med.StatutBDM);
                cmd.Parameters.AddWithValue("@EU", med.NumAutorisationEU);
                cmd.Parameters.AddWithValue("@Tit", med.Titulaires);
                cmd.Parameters.AddWithValue("@Surv", med.SurveillanceRenforcee);
                cmd.Parameters.AddWithValue("@Search", searchText);
                await cmd.ExecuteNonQueryAsync();
            }

            Debug.WriteLine($"[BDPMService] {medicaments.Count} médicaments insérés");

            // Insérer les présentations
            foreach (var pres in presentations)
            {
                var sql = @"INSERT INTO Presentations
                    (CIP13, CIS, CIP7, Libelle, StatutAdministratif, EtatCommercialisation, DateDeclaration)
                    VALUES (@CIP13, @CIS, @CIP7, @Lib, @Statut, @Etat, @Date)";

                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@CIP13", pres.CIP13);
                cmd.Parameters.AddWithValue("@CIS", pres.CIS);
                cmd.Parameters.AddWithValue("@CIP7", pres.CIP7);
                cmd.Parameters.AddWithValue("@Lib", pres.Libelle);
                cmd.Parameters.AddWithValue("@Statut", pres.StatutAdministratif);
                cmd.Parameters.AddWithValue("@Etat", pres.EtatCommercialisation);
                cmd.Parameters.AddWithValue("@Date", pres.DateDeclaration);
                await cmd.ExecuteNonQueryAsync();
            }

            Debug.WriteLine($"[BDPMService] {presentations.Count} présentations insérées");

            // Insérer les compositions
            foreach (var compo in compositions)
            {
                var sql = @"INSERT INTO Compositions
                    (CIS, DesignationElement, CodeSubstance, DenominationSubstance, Dosage, ReferenceDosage, NatureComposant, NumeroLiaison)
                    VALUES (@CIS, @Design, @Code, @Denom, @Dosage, @Ref, @Nature, @Num)";

                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@CIS", compo.CIS);
                cmd.Parameters.AddWithValue("@Design", compo.DesignationElement);
                cmd.Parameters.AddWithValue("@Code", compo.CodeSubstance);
                cmd.Parameters.AddWithValue("@Denom", compo.DenominationSubstance);
                cmd.Parameters.AddWithValue("@Dosage", compo.Dosage);
                cmd.Parameters.AddWithValue("@Ref", compo.ReferenceDosage);
                cmd.Parameters.AddWithValue("@Nature", compo.NatureComposant);
                cmd.Parameters.AddWithValue("@Num", compo.NumeroLiaison);
                await cmd.ExecuteNonQueryAsync();
            }

            Debug.WriteLine($"[BDPMService] {compositions.Count} compositions insérées");

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            Debug.WriteLine($"[BDPMService] ❌ Erreur insertion: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Recherche des médicaments par nom (autocomplétion)
    /// </summary>
    public async Task<List<Medicament>> SearchMedicamentsAsync(string query, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
            return new List<Medicament>();

        if (!File.Exists(_databasePath))
            return new List<Medicament>();

        try
        {
            // Normaliser la requête (enlever accents)
            var normalizedQuery = query.ToLowerInvariant()
                .Replace("é", "e").Replace("è", "e").Replace("ê", "e")
                .Replace("à", "a").Replace("â", "a")
                .Replace("ô", "o").Replace("ù", "u").Replace("û", "u")
                .Replace("ï", "i").Replace("î", "i")
                .Replace("ç", "c");

            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync();

            var sql = @"
                SELECT CIS, Denomination, Forme, VoieAdministration, StatutAMM,
                       TypeProcedure, Commercialisation, DateAMM, StatutBDM,
                       NumAutorisationEU, Titulaires, SurveillanceRenforcee
                FROM Medicaments
                WHERE SearchText LIKE @Query
                AND Commercialisation = 'Commercialisée'
                ORDER BY
                    CASE
                        WHEN SearchText LIKE @QueryStart THEN 1
                        ELSE 2
                    END,
                    Denomination
                LIMIT @Limit";

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@Query", $"%{normalizedQuery}%");
            command.Parameters.AddWithValue("@QueryStart", $"{normalizedQuery}%");
            command.Parameters.AddWithValue("@Limit", limit);

            var medicaments = new List<Medicament>();

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var medicament = new Medicament
                {
                    CIS = reader.GetString(0),
                    Denomination = reader.GetString(1),
                    Forme = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    VoieAdministration = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    StatutAMM = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    TypeProcedure = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Commercialisation = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    DateAMM = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    StatutBDM = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    NumAutorisationEU = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    Titulaires = reader.IsDBNull(10) ? "" : reader.GetString(10),
                    SurveillanceRenforcee = reader.IsDBNull(11) ? "" : reader.GetString(11)
                };

                // Charger les présentations pour ce médicament
                medicament.Presentations = await GetPresentationsAsync(connection, medicament.CIS);

                // Charger les compositions (DCI)
                medicament.Compositions = await GetCompositionsAsync(connection, medicament.CIS);

                medicaments.Add(medicament);
            }

            return medicaments;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BDPMService] ❌ Erreur recherche: {ex.Message}");
            return new List<Medicament>();
        }
    }

    /// <summary>
    /// Récupère les présentations d'un médicament
    /// </summary>
    private async Task<List<MedicamentPresentation>> GetPresentationsAsync(SqliteConnection connection, string cis)
    {
        var presentations = new List<MedicamentPresentation>();

        var sql = @"
            SELECT CIP13, CIS, CIP7, Libelle, StatutAdministratif, EtatCommercialisation, DateDeclaration
            FROM Presentations
            WHERE CIS = @CIS
            AND EtatCommercialisation = 'Commercialisée'
            ORDER BY Libelle";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@CIS", cis);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            presentations.Add(new MedicamentPresentation
            {
                CIP13 = reader.GetString(0),
                CIS = reader.GetString(1),
                CIP7 = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Libelle = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StatutAdministratif = reader.IsDBNull(4) ? "" : reader.GetString(4),
                EtatCommercialisation = reader.IsDBNull(5) ? "" : reader.GetString(5),
                DateDeclaration = reader.IsDBNull(6) ? "" : reader.GetString(6)
            });
        }

        return presentations;
    }

    /// <summary>
    /// Récupère les compositions (DCI) d'un médicament
    /// </summary>
    private async Task<List<MedicamentComposition>> GetCompositionsAsync(SqliteConnection connection, string cis)
    {
        var compositions = new List<MedicamentComposition>();

        var sql = @"
            SELECT CIS, DesignationElement, CodeSubstance, DenominationSubstance,
                   Dosage, ReferenceDosage, NatureComposant, NumeroLiaison
            FROM Compositions
            WHERE CIS = @CIS
            ORDER BY DenominationSubstance";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@CIS", cis);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            compositions.Add(new MedicamentComposition
            {
                CIS = reader.GetString(0),
                DesignationElement = reader.IsDBNull(1) ? "" : reader.GetString(1),
                CodeSubstance = reader.IsDBNull(2) ? "" : reader.GetString(2),
                DenominationSubstance = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Dosage = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ReferenceDosage = reader.IsDBNull(5) ? "" : reader.GetString(5),
                NatureComposant = reader.IsDBNull(6) ? "" : reader.GetString(6),
                NumeroLiaison = reader.IsDBNull(7) ? "" : reader.GetString(7)
            });
        }

        return compositions;
    }

    /// <summary>
    /// Vérifie si la base BDPM existe et est initialisée
    /// </summary>
    public bool IsDatabaseInitialized()
    {
        return File.Exists(_databasePath);
    }

    /// <summary>
    /// Obtient la date de dernière mise à jour de la base
    /// </summary>
    public DateTime? GetLastUpdateDate()
    {
        if (!File.Exists(_databasePath))
            return null;

        return File.GetLastWriteTime(_databasePath);
    }

    /// <summary>
    /// Obtient le nombre de médicaments dans la base
    /// </summary>
    public async Task<int> GetMedicamentsCountAsync()
    {
        if (!File.Exists(_databasePath))
            return 0;

        try
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync();

            var sql = "SELECT COUNT(*) FROM Medicaments WHERE Commercialisation = 'Commercialisée'";

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            var result = await command.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 0;
        }
        catch
        {
            return 0;
        }
    }
}
