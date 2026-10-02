using AchatRatio.Models;

namespace AchatRatio.Services;

public record ResultatRatio(
    int DureeVieJours,
    double DureeVieAnnees,
    decimal CoutParAn,
    decimal CoutParMois,
    decimal? PrixParPoint,
    double? Score,          // 0..100, null si non évaluable
    char? Lettre,           // A..E
    bool EstimationFiable); // false => "estimation préliminaire"

public static class RatioCalculator
{
    /// <summary>Coût annuel de référence par catégorie (ScorePrix = 50 à ce niveau).</summary>
    public static readonly Dictionary<string, decimal> Benchmarks = new()
    {
        ["Vêtements"]       = 25m,
        ["Chaussures"]      = 25m,
        ["Électroménager"]  = 60m,
        ["High-Tech"]       = 150m,
        ["Meubles"]         = 40m,
        ["Sport & Loisirs"] = 30m,
        ["Divers"]          = 50m,
    };

    /// <summary>En dessous, la durée observée est trop courte pour être parlante.</summary>
    public const int JoursMinFiabilite = 45;

    public static ResultatRatio Calculer(Article a, DateOnly? aujourdHui = null)
    {
        var today = aujourdHui ?? DateOnly.FromDateTime(DateTime.Today);

        var fin = a.Statut == StatutArticle.Termine && a.DateFin is not null
                    ? a.DateFin.Value
                    : today;

        var jours  = Math.Max(0, fin.DayNumber - a.DateAchat.DayNumber);
        var annees = jours / 365.25;
        var mois   = jours / 30.44;

        var coutAn   = annees > 0 ? a.PrixAchat / (decimal)annees : a.PrixAchat;
        var coutMois = mois  > 0 ? a.PrixAchat / (decimal)mois   : a.PrixAchat;

        var prixParPoint = a.Note > 0 ? a.PrixAchat / a.Note : (decimal?)null;

        double? score = null;
        char? lettre  = null;
        var fiable     = jours >= JoursMinFiabilite;

        if (fiable && a.Note > 0)
        {
            var benchmark = Benchmarks.TryGetValue(a.Categorie, out var b) ? b : Benchmarks["Divers"];
            var scorePrix = 100.0 / (1.0 + (double)(coutAn / benchmark));
            var scoreNote = a.Note * 10.0;

            score  = Math.Round(0.5 * scoreNote + 0.5 * scorePrix, 1);
            lettre = score switch
            {
                >= 80 => 'A',
                >= 65 => 'B',
                >= 50 => 'C',
                >= 35 => 'D',
                _     => 'E'
            };
        }

        return new ResultatRatio(jours, annees, coutAn, coutMois,
                                 prixParPoint, score, lettre, fiable);
    }

    /// <summary>Point mort entre deux options d'achat. Retourne le nombre d'années
    /// après lequel l'option premium devient moins chère, ou null si jamais.</summary>
    public static double? PointMort(decimal prix1, double duree1Annees,
                                    decimal prix2, double duree2Annees)
    {
        var taux1 = (double)(prix1 / (decimal)duree1Annees);
        var taux2 = (double)(prix2 / (decimal)duree2Annees);
        if (taux1 <= taux2) return null;
        return (double)(prix2 - prix1) / (taux1 - taux2);
    }

    /// <summary>Économie réalisée en achetant la qualité plutôt que le bas de gamme,
    /// mesurée sur la durée de vie réelle constatée de l'article.</summary>
    public static (decimal Economie, int RemplacementsEvites)? EstimerEconomie(Article a)
    {
        if (a.PrixBasDeGamme is null || a.PrixBasDeGamme <= 0 || a.DureeVieBasDeGammeMois is null)
            return null;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var fin = a.Statut == StatutArticle.Termine && a.DateFin is not null
                    ? a.DateFin.Value : today;

        var anneesReelles = Math.Max(0, fin.DayNumber - a.DateAchat.DayNumber) / 365.25;
        var dureeLow = a.DureeVieBasDeGammeMois.Value / 12.0;

        if (anneesReelles <= 0 || dureeLow <= 0) return null;

        var achatsLowCostNecessaires = (int)Math.Ceiling(anneesReelles / dureeLow);
        return (a.PrixBasDeGamme.Value * achatsLowCostNecessaires - a.PrixAchat,
                achatsLowCostNecessaires - 1);
    }

    /// <summary>1,77 an → "1 an et 9 mois".</summary>
    public static string FormatAnnees(double annees)
    {
        var a = (int)Math.Floor(annees);
        var m = (int)Math.Round((annees - a) * 12);
        if (m == 12) { a++; m = 0; }
        return (a, m) switch
        {
            (0, 0) => "moins d'un mois",
            (0, _) => $"{m} mois",
            (_, 0) => $"{a} an{(a > 1 ? "s" : "")}",
            _      => $"{a} an{(a > 1 ? "s" : "")} et {m} mois"
        };
    }

    public static string FormatJours(double jours) => FormatAnnees(jours / 365.25);
}
