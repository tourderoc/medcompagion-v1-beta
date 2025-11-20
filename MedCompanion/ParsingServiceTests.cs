using System;

namespace MedCompanion
{
    /// <summary>
    /// Tests pour valider le ParsingService
    /// </summary>
    public class ParsingServiceTests
    {
        public static void RunTests()
        {
            var parser = new ParsingService();
            
            Console.WriteLine("=== TESTS PARSING DOCTOLIB ===\n");
            
            // Test 1: Bloc exact fourni
            Console.WriteLine("Test 1: Bloc exact (David FROMENTIN)");
            var test1 = @"David
né(e) FROMENTIN
H, 01/04/2021 (4 ans 6 mois)";
            
            var result1 = parser.ParseDoctolibBlock(test1);
            Console.WriteLine($"Success: {result1.Success}");
            Console.WriteLine($"Prénom: {result1.Prenom}");
            Console.WriteLine($"Nom: {result1.Nom}");
            Console.WriteLine($"Sexe: {result1.Sex}");
            Console.WriteLine($"DOB: {result1.Dob}");
            Console.WriteLine($"Âge: {result1.AgeText}");
            Console.WriteLine();
            
            // Test 2: Variante avec tirets et "nee"
            Console.WriteLine("Test 2: Variante (Jade MARTIN avec -)");
            var test2 = @"Jade
nee MARTIN
F, 11-02-2015 (9 ans)";
            
            var result2 = parser.ParseDoctolibBlock(test2);
            Console.WriteLine($"Success: {result2.Success}");
            Console.WriteLine($"Prénom: {result2.Prenom}");
            Console.WriteLine($"Nom: {result2.Nom}");
            Console.WriteLine($"Sexe: {result2.Sex}");
            Console.WriteLine($"DOB: {result2.Dob}");
            Console.WriteLine($"Âge: {result2.AgeText}");
            Console.WriteLine();
            
            // Test 3: Avec M au lieu de H
            Console.WriteLine("Test 3: M mappé vers H");
            var test3 = @"Marc
né(e) DUPONT
M, 15/03/2018 (6 ans)";
            
            var result3 = parser.ParseDoctolibBlock(test3);
            Console.WriteLine($"Success: {result3.Success}");
            Console.WriteLine($"Prénom: {result3.Prenom}");
            Console.WriteLine($"Nom: {result3.Nom}");
            Console.WriteLine($"Sexe: {result3.Sex} (devrait être H)");
            Console.WriteLine($"DOB: {result3.Dob}");
            Console.WriteLine();
            
            // Test 4: Seulement 2 lignes (prénom + nom)
            Console.WriteLine("Test 4: Seulement prénom et nom");
            var test4 = @"Sophie
née BERNARD";
            
            var result4 = parser.ParseDoctolibBlock(test4);
            Console.WriteLine($"Success: {result4.Success}");
            Console.WriteLine($"Prénom: {result4.Prenom}");
            Console.WriteLine($"Nom: {result4.Nom}");
            Console.WriteLine($"Sexe: {result4.Sex ?? "null"}");
            Console.WriteLine($"DOB: {result4.Dob ?? "null"}");
            Console.WriteLine();
            
            // Test 5: Avec texte restant (note brute)
            Console.WriteLine("Test 5: Avec texte restant pour note brute");
            var test5 = @"David
né(e) FROMENTIN
H, 01/04/2021 (4 ans 6 mois)

Motif de consultation: troubles du sommeil
Observation: l'enfant présente des difficultés d'endormissement";
            
            var result5 = parser.ParseDoctolibBlock(test5);
            Console.WriteLine($"Success: {result5.Success}");
            Console.WriteLine($"Prénom: {result5.Prenom}");
            Console.WriteLine($"Nom: {result5.Nom}");
            Console.WriteLine($"Sexe: {result5.Sex}");
            Console.WriteLine($"DOB: {result5.Dob}");
            Console.WriteLine($"Texte restant: {result5.RemainingText}");
            Console.WriteLine();
            
            // Test 6: Espaces multiples et accents
            Console.WriteLine("Test 6: Tolérance espaces et accents");
            var test6 = @"  Marie-Claire  
  née    LAURENT  
  F  ,  23/08/2019  ( 5 ans ) ";
            
            var result6 = parser.ParseDoctolibBlock(test6);
            Console.WriteLine($"Success: {result6.Success}");
            Console.WriteLine($"Prénom: {result6.Prenom}");
            Console.WriteLine($"Nom: {result6.Nom}");
            Console.WriteLine($"Sexe: {result6.Sex}");
            Console.WriteLine($"DOB: {result6.Dob}");
            Console.WriteLine();
            
            // Test 7: Format simple (fallback)
            Console.WriteLine("Test 7: Format simple (fallback)");
            var test7 = "Jean Dupont";
            
            var result7 = parser.ParseDoctolibBlock(test7);
            Console.WriteLine($"Success: {result7.Success} (devrait être false pour fallback)");
            
            var (prenom, nom) = parser.ParseSimpleFormat(test7);
            Console.WriteLine($"Format simple - Prénom: {prenom}, Nom: {nom}");
            Console.WriteLine();
            
            Console.WriteLine("=== FIN DES TESTS ===");
        }
    }
}
