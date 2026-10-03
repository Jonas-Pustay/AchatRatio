using AchatRatio.Models;

namespace AchatRatio.Services;

public record ResultatRatio(
    int DureeVieJours,
    double DureeVieAnnees,
    decimal CoutParAn,
    decimal CoutParMois,
    decimal CoutParJour,
    decimal? PrixParPoint,
    double? Score,          // 0..100, null si non évaluable
    char? Lettre,           // A..E
    bool EstimationFiable)
{
    /// <summary>Unité « naturelle » selon la durée d'usage :
    /// /jour sous 30 jours, /mois jusqu'à 1 an, /an au-delà.</summary>
    public string UniteNaturelle => DureeVieJours switch
    {
        < RatioCalculator.SeuilMois => "/jour",
        < RatioCalculator.SeuilAn   => "/mois",
        _                           => "/an"
    };

    /// <summary>Coût dans l'unité naturelle.</summary>
    public decimal CoutNaturel => UniteNaturelle switch
    {
        "/jour" => CoutParJour,
        "/mois" => CoutParMois,
        _       => CoutParAn
    };

    /// <summary>Vue annualisée : tout est ramené en €/an.</summary>
    public decimal CoutVue(bool vueAnnuelle) => vueAnnuelle ? CoutParAn : CoutNaturel;
    public string UniteVue(bool vueAnnuelle) => vueAnnuelle ? "/an" : UniteNaturelle;

    /// <summary>Annualiser un usage constaté de moins d'un an est une extrapolation
    /// (on suppose le rythme constant sur l'année) → mérite un « ≈ ».</summary>
    public bool VueAnnuelleEstProjection => DureeVieJours < 365;
}

public static class RatioCalculator
{
    /// <summary>Benchmarks de coût ANNUEL par catégorie (ScorePrix = 50 à ce
    /// niveau). Ce sont les curseurs de calibration de l'app.</summary>
    public static readonly Dictionary<string, decimal> Benchmarks = new()
    {
        // Biens durables
        ["Vêtements"]       = 25m,
        ["Chaussures"]      = 25m,
        ["Électroménager"]  = 60m,
        ["High-Tech"]       = 150m,
        ["Meubles"]         = 40m,
        ["Sport & Loisirs"] = 30m,

        // Consommables — benchmarks en équivalent annuel
        ["Hygiène & Soins"] = 50m,
        ["Entretien"]       = 60m,
        ["Alimentation"]    = 250m,

        ["Divers"]          = 50m,
    };

    /// <summary>Un article EN COURS a besoin de cette durée d'usage pour qu'une
    /// estimation commence à être parlante. Un article TERMINÉ est une mesure
    /// définitive, quelle que soit sa durée (mayonnaise finie en 2 semaines…).</summary>
    public const int JoursMinFiabilite = 45;
    /// <summary>Seuils d'affichage, en jours d'usage : sous SeuilMois → jours et
    /// €/jour ; de SeuilMois à SeuilAn-1 → mois et €/mois ; à partir de SeuilAn →
    /// années et €/an. Les deux curseurs de l'affichage — la durée affichée
    /// (FormatAnnees) suit exactement les mêmes frontières.</summary>
    public const int SeuilMois = 30;
    public const int SeuilAn = 365;

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
        var coutJour = jours > 0 ? a.PrixAchat / jours           : a.PrixAchat;

        var prixParPoint = a.Note > 0 ? a.PrixAchat / a.Note : (decimal?)null;

        var fiable = jours >= 1
                  && (a.Statut == StatutArticle.Termine || jours >= JoursMinFiabilite);

        double? score = null;
        char? lettre  = null;
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

        return new ResultatRatio(jours, annees, coutAn, coutMois, coutJour,
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
    /// sur la durée de vie réelle constatée. Durée en mois décimaux (0,25 ≈ 1 semaine).</summary>
    public static (decimal Economie, int RemplacementsEvites)? EstimerEconomie(Article a)
    {
        if (a.PrixBasDeGamme is null || a.PrixBasDeGamme <= 0 || a.DureeVieBasDeGammeMois is null)
            return null;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var fin = a.Statut == StatutArticle.Termine && a.DateFin is not null
                    ? a.DateFin.Value : today;

        var anneesReelles = Math.Max(0, fin.DayNumber - a.DateAchat.DayNumber) / 365.25;
        var dureeLow = (double)a.DureeVieBasDeGammeMois.Value / 12.0;

        if (anneesReelles <= 0 || dureeLow <= 0) return null;

        var achatsLowCostNecessaires = (int)Math.Ceiling(anneesReelles / dureeLow);
        return (a.PrixBasDeGamme.Value * achatsLowCostNecessaires - a.PrixAchat,
                achatsLowCostNecessaires - 1);
    }

    /// <summary>29 jours → "29 jours" · 40 jours → "1 mois" · 364 jours → "12 mois" ·
    /// 2,1 ans → "2 ans et 1 mois". Suit les mêmes seuils que l'unité (SeuilMois/SeuilAn).</summary>
    public static string FormatAnnees(double annees)
    {
        var jours = annees * 365.25;

        if (jours < SeuilMois)          // moins de 30 jours : en jours
        {
            var n = Math.Max(1, (int)Math.Round(jours));
            return $"{n} jour{(n > 1 ? "s" : "")}";
        }

        if (jours < SeuilAn)            // de 30 jours à 1 an : en mois
        {
            var m = Math.Max(1, (int)Math.Round(jours / 30.44));
            return $"{m} mois";
        }

        // 1 an et plus
        var a = (int)Math.Floor(annees);
        var r = (int)Math.Round((annees - a) * 12);
        if (r == 12) { a++; r = 0; }
        return r == 0
            ? $"{a} an{(a > 1 ? "s" : "")}"
            : $"{a} an{(a > 1 ? "s" : "")} et {r} mois";
    }

    public static string FormatJours(double jours) => FormatAnnees(jours / 365.25);
}
