using System;
using System.IO;
using System.Text.RegularExpressions;
using NotchFlow.Core.Activities;
using NotchFlow.Core.Animation;
using NotchFlow.Core.Scenes;
using Xunit;

namespace NotchFlow.Core.Tests;

/// <summary>
/// Le langage visuel, vérifié là où il se calcule.
///
/// Ces tests ne regardent pas des pixels : ils vérifient les relations qui font
/// qu'une forme est cohérente — un congé qui passe par ses extrémités, un congé
/// continu plus plein qu'un arc, une hauteur de carte qui est la somme de son
/// contenu, un rayon intérieur jamais supérieur à son conteneur. Ce sont
/// exactement les relations qu'un réglage isolé finit par casser sans que rien ne
/// le signale.
/// </summary>
public class DesignLanguageTests
{
    private const double Tolerance = 0.001;

    // ------------------------------------------------------------------
    // Silhouette
    // ------------------------------------------------------------------

    [Fact]
    public void Silhouette_OfAnEmptyForm_ProducesNothing()
    {
        Assert.Empty(IslandShape.Silhouette(0, 40, 12));
        Assert.Empty(IslandShape.Silhouette(200, 0, 12));
    }

    [Fact]
    public void Silhouette_WithoutCornerRadius_IsARectangle()
    {
        ShapePoint[] points = IslandShape.Silhouette(200, 40, 0);

        Assert.Equal(4, points.Length);
        Assert.Equal(new ShapePoint(0, 0), points[0]);
        Assert.Equal(new ShapePoint(200, 0), points[1]);
        Assert.Equal(new ShapePoint(200, 40), points[2]);
        Assert.Equal(new ShapePoint(0, 40), points[3]);
    }

    [Fact]
    public void Silhouette_StartsFlatAtTheTopEdge()
    {
        // Les deux coins supérieurs sont droits, et à la même ordonnée : c'est ce
        // qui fait tenir l'Island au bord de l'écran. Un arrondi en haut la
        // détacherait de la surface sur laquelle elle repose.
        ShapePoint[] points = IslandShape.Silhouette(240, 52, 12);

        Assert.Equal(0, points[0].Y);
        Assert.Equal(0, points[1].Y);
        Assert.Equal(0, points[0].X);
        Assert.Equal(240, points[1].X);
    }

    [Fact]
    public void Silhouette_StaysInsideItsOwnBounds()
    {
        ShapePoint[] points = IslandShape.Silhouette(240, 52, 12, IslandShape.Squircle);

        Assert.All(points, point =>
        {
            Assert.InRange(point.X, 0, 240);
            Assert.InRange(point.Y, 0, 52);
        });
    }

    [Fact]
    public void Silhouette_WithAnExcessiveRadius_IsClampedInsteadOfInverted()
    {
        // Un rayon plus grand que la moitié de la hauteur produirait une courbe qui
        // se croise. Il est ramené à la borne, sans exception : un réglage poussé à
        // fond ne doit pas faire disparaître l'Island.
        ShapePoint[] points = IslandShape.Silhouette(240, 24, 60, IslandShape.Squircle);

        Assert.All(points, point => Assert.InRange(point.Y, 0, 24));
    }

    [Fact]
    public void Silhouette_TouchesBothEndsOfTheCorner()
    {
        // Extrémités du congé inférieur droit : le point où il quitte l'arête
        // verticale, et celui où il rejoint le bord inférieur. Toute erreur
        // d'exposant s'y voit immédiatement, parce que la courbe doit y passer
        // exactement.
        ShapePoint[] points = IslandShape.Silhouette(240, 52, 12, IslandShape.Squircle);

        Assert.Contains(points, point => Math.Abs(point.X - 240) < Tolerance && Math.Abs(point.Y - 40) < Tolerance);
        Assert.Contains(points, point => Math.Abs(point.X - 228) < Tolerance && Math.Abs(point.Y - 52) < Tolerance);
    }

    [Fact]
    public void SquircleCorner_IsFullerThanACircularArc()
    {
        // C'est la propriété qui justifie la superellipse, et elle se démontre :
        // sur la diagonale du congé, un exposant 2 éloigne la courbe du centre du
        // coin de 19 %, là où un cercle la laisse rentrer. C'est ce qui supprime
        // la discontinuité de courbure à la jonction avec l'arête — le petit
        // accroc qu'on perçoit sans savoir le nommer.
        //
        // Les deux valeurs sont aussi comparées à leur formule, et pas seulement
        // entre elles : un décalage qui affecterait les deux familles de la même
        // façon passerait inaperçu dans une simple comparaison.
        double arc = DiagonalReach(IslandShape.Silhouette(240, 52, 12, IslandShape.Circular), 240, 52, 12);
        double squircle = DiagonalReach(IslandShape.Silhouette(240, 52, 12, IslandShape.Squircle), 240, 52, 12);

        Assert.Equal(12 * Math.Sqrt(0.5), arc, 2);
        Assert.Equal(12 * Math.Sqrt(Math.Sqrt(0.5)), squircle, 2);
        Assert.True(squircle > arc + 0.5, $"Le congé continu devrait être plus plein : {squircle:0.###} contre {arc:0.###}.");
    }

    [Fact]
    public void Silhouette_TruncatedToABand_BecomesAStraightCut()
    {
        // Le reflet est découpé par une bande, et non par l'intersection de deux
        // géométries : borner les ordonnées du contour convexe donne exactement le
        // résultat voulu, ce qui évite un second calculateur de forme.
        ShapePoint[] points = IslandShape.Silhouette(240, 52, 12, IslandShape.Squircle, band: 22);

        Assert.All(points, point => Assert.True(point.Y <= 22 + Tolerance));
        Assert.Contains(points, point => Math.Abs(point.Y - 22) < Tolerance && point.X < Tolerance);
        Assert.Contains(points, point => Math.Abs(point.Y - 22) < Tolerance && Math.Abs(point.X - 240) < Tolerance);
    }

    /// <summary>
    /// Distance du congé inférieur droit au point où il croise sa diagonale —
    /// c'est-à-dire le milieu de la courbe.
    ///
    /// C'est la seule mesure qui compare les familles de congés sur un pied
    /// d'égalité : comparer au même angle ne dit rien, la paramétrisation
    /// n'ayant pas le même sens d'une famille à l'autre, et comparer à la même
    /// abscisse maximale renverrait l'épaule droite dans les deux cas.
    /// </summary>
    private static double DiagonalReach(ShapePoint[] points, double width, double height, double radius)
    {
        double centreX = width - radius;
        double centreY = height - radius;
        double reach = 0;
        double smallestGap = double.MaxValue;

        foreach (ShapePoint point in points)
        {
            double dx = point.X - centreX;
            double dy = point.Y - centreY;

            // Coin inférieur droit uniquement : le coin gauche est symétrique et
            // fausserait la recherche du minimum.
            if (dx <= 0 || dy <= 0 || point.X < width / 2)
            {
                continue;
            }

            double gap = Math.Abs(dx - dy);

            if (gap < smallestGap)
            {
                smallestGap = gap;
                reach = dx;
            }
        }

        Assert.True(reach > 0, "Aucun point sur la diagonale du congé.");

        return reach;
    }

    // ------------------------------------------------------------------
    // Paliers
    // ------------------------------------------------------------------

    [Fact]
    public void Tiers_AreOrderedByHowMuchAttentionTheyAskFor()
    {
        IslandFootprint idle = IslandFootprint.For(IslandPresentationTier.Idle);
        IslandFootprint signal = IslandFootprint.For(IslandPresentationTier.Signal);
        IslandFootprint card = IslandFootprint.For(IslandPresentationTier.Card);

        Assert.True(signal.Width > idle.Width);
        Assert.True(card.Width > signal.Width);
        Assert.True(card.Height > signal.Height);

        // La veille est une lèvre, plus basse que la forme compacte : sans
        // activité, la notch n'a rien à porter et doit se faire presque oublier.
        Assert.True(idle.Height < signal.Height);
    }

    [Fact]
    public void Density_MovesTheAirAndNeverTheType()
    {
        // La hauteur d'une carte est la somme de son contenu : deux marges, une
        // ligne de légende de 14, un écart de 2, une ligne de titre de 16. Les
        // régler séparément déformerait la forme, donc ce test les tient ensemble.
        const double lines = 14 + 2 + 16;

        foreach (IslandContentDensity density in Enum.GetValues<IslandContentDensity>())
        {
            IslandFootprint card = IslandFootprint.For(IslandPresentationTier.Card, density);
            double padding = IslandFootprint.CardVerticalPadding(density);

            Assert.Equal(lines + (2 * padding), card.Height, 3);

            // La largeur suit la marge : sans cela, une carte aérée gagnerait de
            // l'air vertical sans gagner un pixel de texte.
            Assert.True(card.Width > 200);
        }
    }

    [Fact]
    public void Density_LeavesTheSmallerTiersAlone()
    {
        foreach (IslandContentDensity density in Enum.GetValues<IslandContentDensity>())
        {
            Assert.Equal(IslandFootprint.Idle, IslandFootprint.For(IslandPresentationTier.Idle, density));
            Assert.Equal(IslandFootprint.Signal, IslandFootprint.For(IslandPresentationTier.Signal, density));
        }
    }

    // ------------------------------------------------------------------
    // Résolution du palier
    // ------------------------------------------------------------------

    [Fact]
    public void NothingAtAll_ShowsTheIdleDot()
    {
        Assert.Equal(IslandPresentationTier.Idle, IslandPresentation.Resolve(null));
    }

    [Fact]
    public void ABackgroundActivity_NeverTakesMoreThanOneLine()
    {
        // C'est la règle qui empêche un lecteur de musique laissé ouvert de
        // transformer l'Island en bandeau permanent : la priorité d'arrière-plan
        // plafonne le palier au signal.
        var activity = new IslandActivity
        {
            Id = "test",
            FeatureId = "test",
            SceneKey = IslandSceneCatalog.Media,
            Title = "Titre",
            Priority = ActivityPriority.Background
        };

        Assert.Equal(IslandPresentationTier.Signal, IslandPresentation.Resolve(activity));
    }

    [Theory]
    [InlineData(ActivityPriority.Normal)]
    [InlineData(ActivityPriority.High)]
    [InlineData(ActivityPriority.Critical)]
    public void AnActivityThatAsksSomething_IsRead(ActivityPriority priority)
    {
        var activity = new IslandActivity
        {
            Id = "test",
            FeatureId = "test",
            SceneKey = IslandSceneCatalog.Card,
            Title = "Titre",
            Priority = priority
        };

        Assert.Equal(IslandPresentationTier.Card, IslandPresentation.Resolve(activity));
    }

    [Fact]
    public void AnExplicitTier_OverridesPriority()
    {
        // Un transfert de plusieurs heures est peu urgent mais mérite d'être lu :
        // les deux dimensions divergent légitimement, et la surcharge existe pour
        // ce cas précis.
        var activity = new IslandActivity
        {
            Id = "test",
            FeatureId = "test",
            SceneKey = IslandSceneCatalog.Card,
            Title = "Titre",
            Priority = ActivityPriority.Background,
            Presentation = IslandPresentationTier.Card
        };

        Assert.Equal(IslandPresentationTier.Card, IslandPresentation.Resolve(activity));
    }

    // ------------------------------------------------------------------
    // Mouvement
    // ------------------------------------------------------------------

    [Fact]
    public void TheSpringIsDescribedByItsReactionTimeAndItsBounce()
    {
        // Les deux vocabulaires sont reliés par les identités d'un oscillateur
        // amorti, donc la traduction doit être exacte dans les deux sens. Un écart
        // ici signifierait que ce que l'utilisateur règle n'est pas ce qui bouge.
        SpringParameters parameters = SpringParameters.FromResponse(0.43, 0.78);

        Assert.Equal(0.43, parameters.ResponseSeconds, 3);
        Assert.Equal(0.78, parameters.DampingRatio, 3);
    }

    [Fact]
    public void HoverIsMoreElasticThanOpening()
    {
        // Le survol annonce, l'ouverture installe : le premier dépasse, le second
        // se contente d'à peine dépasser. C'est cette différence qui porte le
        // rebond visible, l'Island n'ayant qu'un bord libre.
        Assert.True(SpringParameters.Hover.DampingRatio < SpringParameters.Default.DampingRatio);
        Assert.True(SpringParameters.Hover.ResponseSeconds < SpringParameters.Default.ResponseSeconds);
    }

    [Fact]
    public void AnOutOfRangeResponseTime_IsClamped()
    {
        Assert.True(SpringParameters.FromResponse(0, 0.78).ResponseSeconds >= 0.15);
        Assert.True(SpringParameters.FromResponse(100, 5.0).DampingRatio <= 1.5);
    }

    // ------------------------------------------------------------------
    // Concentricité des congés
    // ------------------------------------------------------------------

    [Fact]
    public void NoInnerCornerRadius_ReachesTheContainer()
    {
        // Règle de concentricité : un rayon intérieur ne peut pas atteindre celui
        // de son conteneur, sans quoi les deux courbes se croisent visiblement.
        //
        // La vérification porte sur la géométrie de référence, et c'est une
        // limite assumée : le rayon de l'Island est un réglage, les jetons
        // internes ne le sont pas. Un utilisateur qui descend le rayon sous le
        // plus grand jeton interne quitte donc la règle — les surfaces internes
        // restent en retrait d'au moins 10 DIP, si bien qu'aucune courbe ne
        // croise l'autre, mais la relation n'est plus tenue par les jetons.
        // C'est pour cela que le plancher du rayon vaut 4 DIP et non 20 : un
        // plancher qui obéirait à la règle interdirait toute forme discrète.
        string tokens = ReadTokensFile();

        double chip = ReadToken(tokens, "NfRadiusChip");
        double control = ReadToken(tokens, "NfRadiusControl");
        double inner = ReadToken(tokens, "NfRadiusInner");
        double container = new NotchFlow.Infrastructure.Config.AppSettings().CornerRadiusBottom;

        Assert.True(chip < control, $"Le rayon de pastille ({chip}) atteint celui de contrôle ({control}).");
        Assert.True(control < inner, $"Le rayon de contrôle ({control}) atteint celui de surface interne ({inner}).");
        Assert.True(inner < container, $"Le rayon interne ({inner}) atteint celui du conteneur de référence ({container}).");
    }

    /// <summary>
    /// Chemin du fichier de jetons, résolu depuis la sortie de compilation.
    ///
    /// La remontée est explicite plutôt que devinée : un chemin relatif figé
    /// casserait dès que la profondeur de sortie changerait, et le test échouerait
    /// alors pour une raison qui n'a rien à voir avec ce qu'il vérifie.
    /// </summary>
    private static string ReadTokensFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "src",
                "NotchFlow.App",
                "UI",
                "Themes",
                "Tokens.xaml");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Fichier de jetons introuvable depuis " + AppContext.BaseDirectory);
    }

    private static double ReadToken(string tokens, string key)
    {
        Match match = Regex.Match(
            tokens,
            $"x:Key=\"{key}\">([0-9.]+)</x:Double>",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, $"Jeton {key} absent des jetons de conception.");

        return double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
