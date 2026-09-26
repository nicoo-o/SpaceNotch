using System;
using System.Collections.Generic;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Infrastructure.Config;

/// <summary>
/// Mode de composition du fond de l'Island.
///
/// Les deux voies sont conservées et exposées à l'utilisateur (voir ADR-006) :
/// le fond reposant sur le compositeur est le plus fidèle, mais il dépend du
/// réglage système « effets de transparence ». Le mode opaque reste disponible
/// comme repli déterministe.
/// </summary>
public enum IslandBackdropMode
{
    /// <summary>Choisi automatiquement d'après les capacités et les réglages du système.</summary>
    Auto = 0,

    /// <summary>
    /// Backdrop transparent du compositeur : le fondu du bas se dissout
    /// réellement dans le bureau. C'est le rendu de référence.
    /// </summary>
    Transparent = 1,

    /// <summary>
    /// Flou du contenu situé derrière l'Island, obtenu par le même chemin que le
    /// mode transparent. Ne dépend pas du mode économie d'énergie.
    /// </summary>
    Blurred = 2,

    /// <summary>
    /// Surface quasi opaque : ne dépend d'aucun réglage système, au prix de la
    /// disparition de l'effet de dissolution.
    /// </summary>
    Opaque = 3
}

/// <summary>
/// Moniteur d'ancrage de l'Island.
/// </summary>
public enum IslandDisplayMode
{
    /// <summary>Écran principal uniquement.</summary>
    Primary = 0,

    /// <summary>Écran qui contient le pointeur au moment de l'affichage.</summary>
    Current = 1,

    /// <summary>Écran explicitement désigné.</summary>
    Custom = 2
}

/// <summary>
/// Emplacement de la découpe caméra / encoche, lorsque l'utilisateur en déclare
/// une. Windows n'expose aucune notion universelle de notch : la configuration
/// est donc explicite.
/// </summary>
public enum CameraCutoutMode
{
    None = 0,

    /// <summary>Centré horizontalement, hauteur standard.</summary>
    Center = 1,

    Left = 2,

    Right = 3,

    /// <summary>Position et taille définies manuellement, en DIPs.</summary>
    Custom = 4
}

/// <summary>Thème de l'Island.</summary>
/// <summary>Sensation de la pastille qui suit la main (ADR-019).</summary>
public enum DetachFeel
{
    /// <summary>Plus de retard et un rebond plus ample.</summary>
    Soft = 0,

    /// <summary>La référence : un léger retard, un rebond à l'arrêt.</summary>
    Natural = 1,

    /// <summary>Presque collée à la main, un rebond bref.</summary>
    Firm = 2
}

/// <summary>Teinte de la surface de la notch, de la languette, de la pastille et de la bulle.</summary>
public enum SurfaceTint
{
    /// <summary>Noir pur : la référence.</summary>
    Oled = 0,

    /// <summary>Gris très sombre, un peu moins tranché.</summary>
    Graphite = 1,

    /// <summary>Couleur choisie par l'utilisateur.</summary>
    Custom = 2
}

public enum IslandAppearance
{
    Dark = 0,
    Light = 1,
    Auto = 2
}

/// <summary>
/// Préférences persistées dans <c>%AppData%\SpaceNotch\config.json</c>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Facteurs appliqués au survol : le même ressort, plus vif et plus élastique.
    ///
    /// Ce n'est pas un second réglage utilisateur mais une déclinaison du
    /// premier : le survol doit se lire comme le même objet effleuré, pas comme
    /// un objet réglé différemment.
    /// </summary>
    private const double HoverResponseFactor = 0.72;

    private const double HoverBounceFactor = 0.78;

    // ---- Apparence --------------------------------------------------------

    public IslandAppearance Appearance { get; set; } = IslandAppearance.Dark;

    /// <summary>Mode de composition du fond. Voir ADR-006.</summary>
    public IslandBackdropMode BackdropMode { get; set; } = IslandBackdropMode.Auto;

    /// <summary>
    /// Porte la dissolution par une surface calculée par le compositeur.
    ///
    /// Le rendu de référence est celui du compositeur ; ce réglage existe pour
    /// pouvoir le comparer à la solution de repli — un dégradé XAML — sur la même
    /// machine, et pour offrir une issue si un pilote graphique se comportait mal.
    /// Voir ADR-008 et ADR-013.
    /// </summary>
    public bool UseCompositionAtmosphere { get; set; } = true;

    // ---- Géométrie --------------------------------------------------------

    /// <summary>
    /// Densité du contenu des cartes. Remplace les anciennes largeur et hauteur
    /// réglables : la hauteur d'une carte est la somme de son contenu, pas un
    /// curseur. Voir <see cref="IslandFootprint"/> et ADR-012.
    /// </summary>
    public IslandContentDensity Density { get; set; } = IslandContentDensity.Comfortable;

    /// <summary>
    /// Mode géométrique. Il n'en existe qu'un — TopAttached — et il n'est pas
    /// réglable : exposé en lecture seule pour que le code qui dessine la forme
    /// le nomme au lieu de le supposer. Voir ADR-017.
    /// </summary>
    public static IslandGeometryMode GeometryMode => NotchGeometry.Mode;

    /// <summary>
    /// Rayon des congés du bas à la hauteur compacte, en DIPs.
    ///
    /// Le nom est conservé pour relire les configurations existantes. Il ne
    /// désigne plus « le » rayon : le rayon tracé est interpolé entre celui-ci et
    /// <see cref="CornerRadiusExpanded"/> selon la hauteur de la forme, et borné
    /// par ce que la forme peut porter. Voir <see cref="NotchGeometry"/>.
    /// </summary>
    public double CornerRadiusBottom { get; set; } = NotchGeometry.DefaultCompactRadius;

    /// <summary>Rayon des congés du bas d'une forme ouverte, en DIPs.</summary>
    public double CornerRadiusExpanded { get; set; } = NotchGeometry.DefaultExpandedRadius;

    /// <summary>
    /// Rayon des épaules concaves qui raccordent la notch au bord de l'écran, en
    /// DIPs. Zéro donne un raccord à angle droit — toujours collé, jamais
    /// flottant.
    /// </summary>
    public double ShoulderRadius { get; set; } = NotchGeometry.DefaultShoulder;

    /// <summary>
    /// Exposant de la superellipse des congés. 1 décrit un arc de cercle, 2 le
    /// squircle d'iOS. Voir <see cref="IslandShape"/>.
    /// </summary>
    public double CornerSmoothing { get; set; } = IslandShape.Squircle;

    /// <summary>Géométrie résolue, telle que le rendu la trace.</summary>
    public NotchGeometry Geometry => new(CornerRadiusBottom, CornerRadiusExpanded, ShoulderRadius, CornerSmoothing);

    /// <summary>Décalage horizontal du centre, en DIPs. Utile pour se caler sur une caméra décentrée.</summary>
    public double HorizontalOffset { get; set; }

    /// <summary>
    /// Ancien décalage vertical depuis le bord supérieur.
    ///
    /// Conservé uniquement pour relire une configuration ancienne, et toujours
    /// ramené à zéro par <see cref="Sanitize"/> : une notch décalée vers le bas
    /// devient une capsule flottante, ce que la règle n°1 interdit. Voir ADR-017.
    /// </summary>
    public double TopOffset { get; set; }

    // ---- Placement --------------------------------------------------------

    public IslandDisplayMode DisplayMode { get; set; } = IslandDisplayMode.Primary;

    /// <summary>Identifiant du moniteur lorsque <see cref="DisplayMode"/> vaut <c>Custom</c>.</summary>
    public long? CustomDisplayHandle { get; set; }

    public CameraCutoutMode CutoutMode { get; set; } = CameraCutoutMode.None;

    public double CutoutWidth { get; set; }

    public double CutoutHeight { get; set; }

    public double CutoutOffset { get; set; }

    // ---- Animation --------------------------------------------------------

    /// <summary>
    /// Temps de réaction du ressort, en secondes. Plus court = ouverture plus
    /// vive. C'est le premier des deux nombres qui décrivent vraiment un
    /// mouvement ; l'ancienne raideur ne le faisait qu'indirectement.
    /// </summary>
    public double SpringResponseSeconds { get; set; } = 0.46;

    /// <summary>
    /// Rebond, c'est-à-dire le rapport d'amortissement : 1,0 ne dépasse jamais la
    /// cible, 0,55 la dépasse franchement.
    /// </summary>
    public double SpringBounce { get; set; } = 0.58;

    /// <summary>
    /// Préréglage de mouvement choisi dans les réglages. <see cref="MotionStyle.Custom"/>
    /// signifie que l'utilisateur a réglé la vitesse et le rebond à la main.
    /// </summary>
    public MotionStyle MotionStyle { get; set; } = MotionStyle.Natural;

    /// <summary>
    /// Autorise le mouvement hypnotique — la matière vivante qui signale qu'un
    /// travail est en cours. Désactivé, les activités concernées affichent une
    /// image fixe de la même composition : l'information reste, le mouvement
    /// part. Voir ADR-018.
    /// </summary>
    public bool AllowHypnoticMotion { get; set; } = true;

    /// <summary>
    /// Pluie binaire sous la notch pendant un traitement, comme dans la référence
    /// vidéo. Désactivée par défaut : c'est un ornement, et la règle est
    /// qu'aucun ornement ne s'impose.
    /// </summary>
    public bool ShowBinaryRain { get; set; }

    /// <summary>
    /// Applique un préréglage : la vitesse et le rebond prennent ses valeurs.
    /// <see cref="MotionStyle.Custom"/> ne touche à rien.
    /// </summary>
    public void ApplyMotionStyle(MotionStyle style)
    {
        MotionStyle = style;

        if (style == MotionStyle.Custom)
        {
            return;
        }

        SpringParameters spring = MotionPresets.Spring(style);
        SpringResponseSeconds = spring.ResponseSeconds;
        SpringBounce = spring.DampingRatio;
    }

    /// <summary>
    /// Autorise le rebond. Désactivé automatiquement si Windows demande la
    /// réduction des animations ou si le contraste élevé est actif.
    /// </summary>
    public bool AllowBouncyAnimations { get; set; } = true;

    /// <summary>Ressort de l'ouverture et de la fermeture.</summary>
    public SpringParameters Motion => SpringParameters.FromResponse(SpringResponseSeconds, SpringBounce);

    /// <summary>
    /// Ressort du survol : plus vif, plus élastique. C'est lui qui porte le
    /// rebond visible, l'Island n'ayant qu'un bord libre — le bas.
    /// </summary>
    public SpringParameters HoverMotion => SpringParameters.FromResponse(
        SpringResponseSeconds * HoverResponseFactor,
        SpringBounce * HoverBounceFactor);

    // ---- Comportement -----------------------------------------------------

    public bool StartWithWindows { get; set; } = true;

    public bool HoverToPreview { get; set; } = true;

    /// <summary>
    /// Un survol prolongé — environ une seconde — ouvre la notch sans clic.
    /// Désactivé par défaut : le survol court annonce, le clic ouvre, et un
    /// pointeur qui s'attarde en haut de l'écran n'exprime pas toujours une
    /// intention.
    /// </summary>
    public bool HoverToExpand { get; set; }

    /// <summary>
    /// La notch peut être arrachée au bord en la tirant vers le bas, puis posée
    /// librement (ADR-019). Elle revient toujours accrochée au démarrage.
    /// </summary>
    public bool AllowDetach { get; set; } = true;

    /// <summary>
    /// Une activité importante — téléchargement, appel, enregistrement,
    /// priorité critique — prend une bulle à côté de la notch au lieu
    /// d'attendre derrière elle.
    /// </summary>
    public bool ShowSplitBubble { get; set; } = true;

    // ---- Bords et détachement (ADR-019, ADR-020) ----------------------------

    /// <summary>Bord où la notch est accrochée ; retrouvé au démarrage.</summary>
    public NotchEdge DockEdge { get; set; } = NotchEdge.Top;

    /// <summary>Position d'une languette latérale le long du bord, de 0 (haut) à 1 (bas).</summary>
    public double DockOffset { get; set; } = 0.5;

    /// <summary>
    /// Écran où la notch a été accrochée par glisser, repéré par son rectangle
    /// physique « gauche,haut,largeur,hauteur » ; un écran disparu ramène la
    /// notch en haut de l'écran principal.
    /// </summary>
    public string? DockDisplayBounds { get; set; }

    /// <summary>Les côtés gauche et droit accrochent la notch en languette.</summary>
    public bool AllowSideEdges { get; set; } = true;

    /// <summary>Sensation de la pastille qui suit la main.</summary>
    public DetachFeel DetachFeel { get; set; } = DetachFeel.Natural;

    /// <summary>Étirement maximal de la pastille en mouvement, de 0 à 0,10.</summary>
    public double StretchAmount { get; set; } = FluidMotion.MaximumStretch;

    /// <summary>Distance de tirage avant l'arrachement, en DIPs.</summary>
    public double TearDistance { get; set; } = Detachment.TearDistance;

    /// <summary>Les coins et le milieu du bas attirent la pastille lancée.</summary>
    public bool MagnetsEnabled { get; set; } = true;

    /// <summary>L'arrachement et le raccrochage passent par la goutte ; sinon, un simple pop.</summary>
    public bool GooEnabled { get; set; } = true;

    /// <summary>La pastille résiste un peu avant de passer sur un autre écran.</summary>
    public bool MonitorResistance { get; set; } = true;

    // ---- Style ---------------------------------------------------------------

    /// <summary>Teinte de la surface.</summary>
    public SurfaceTint SurfaceTint { get; set; } = SurfaceTint.Oled;

    /// <summary>Couleur personnalisée, « #RRGGBB », quand <see cref="SurfaceTint"/> vaut <c>Custom</c>.</summary>
    public string CustomSurfaceColor { get; set; } = "#14161C";

    /// <summary>Opacité de la surface, de 0,55 à 1 : en dessous, le texte ne se lit plus sur un bureau clair.</summary>
    public double SurfaceOpacity { get; set; } = 1.0;

    /// <summary>Épaules de la languette latérale et de la bulle, en DIPs.</summary>
    public double SideShoulderRadius { get; set; } = SideTab.DefaultShoulder;

    /// <summary>Rayon des coins d'une pastille flottante ouverte, en DIPs.</summary>
    public double FloatingRadius { get; set; } = NotchGeometry.DefaultExpandedRadius;

    /// <summary>Intensité de l'ombre de la pastille flottante et de la languette, de 0 à 0,6.</summary>
    public double FloatingShadowOpacity { get; set; } = 0.30;

    /// <summary>Fin contour autour de la notch, pour les fonds d'écran sombres.</summary>
    public bool ShowOutline { get; set; }

    /// <summary>Opacité du contour, de 0,05 à 0,5.</summary>
    public double OutlineOpacity { get; set; } = 0.14;

    /// <summary>Taille de la bulle.</summary>
    public ElementSize BubbleSize { get; set; } = ElementSize.Normal;

    /// <summary>Taille de la languette latérale.</summary>
    public ElementSize TabSize { get; set; } = ElementSize.Normal;

    /// <summary>Ressort de la pastille qui suit la main, d'après la sensation choisie.</summary>
    public SpringParameters DetachFollowSpring => DetachFeel switch
    {
        DetachFeel.Soft => SpringParameters.FromResponse(0.30, 0.55),
        DetachFeel.Firm => SpringParameters.FromResponse(0.16, 0.75),
        _ => SpringParameters.FromResponse(0.22, 0.62)
    };

    /// <summary>Ce que le lâcher a le droit de faire.</summary>
    public LandingOptions Landing => new(AllowSideEdges, MagnetsEnabled);

    /// <summary>Couleur de surface résolue, en ARGB, opacité comprise.</summary>
    public (byte A, byte R, byte G, byte B) SurfaceColor()
    {
        (byte r, byte g, byte b) = SurfaceTint switch
        {
            SurfaceTint.Graphite => ((byte)0x1A, (byte)0x1B, (byte)0x1F),
            SurfaceTint.Custom when TryParseColor(CustomSurfaceColor, out var custom) => custom,
            _ => ((byte)0, (byte)0, (byte)0)
        };

        return ((byte)Math.Round(Math.Clamp(SurfaceOpacity, 0.55, 1) * 255), r, g, b);
    }

    /// <summary>Lit une couleur « #RRGGBB » ou « RRGGBB ».</summary>
    public static bool TryParseColor(string? text, out (byte R, byte G, byte B) color)
    {
        color = (0, 0, 0);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string hex = text.Trim().TrimStart('#');

        if (hex.Length != 6
            || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int value))
        {
            return false;
        }

        color = ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary>
    /// Retire l'Island lorsqu'une application occupe l'écran — jeu, vidéo plein
    /// écran, présentation.
    ///
    /// Une fenêtre toujours au-dessus se superpose à tout, y compris à ce qui
    /// demande l'écran entier : c'est le seul cas où rester visible est un défaut
    /// et non un service. Une fenêtre simplement agrandie ne déclenche rien, car
    /// elle laisse la place à l'Island et l'utilisateur y travaille encore.
    ///
    /// L'état est demandé au shell via <c>SHQueryUserNotificationState</c>, seule
    /// réponse documentée qui distingue un jeu en Direct3D exclusif d'une fenêtre
    /// agrandie.
    /// </summary>
    public bool HideOverFullscreen { get; set; } = true;

    public bool ShowMedia { get; set; } = true;

    public bool ShowVolumeHud { get; set; } = true;

    public bool ShowNotifications { get; set; } = true;

    public bool ShowBluetooth { get; set; } = true;

    /// <summary>
    /// Surveillance du presse-papier. Désactivée par défaut : aucune donnée n'est
    /// observée tant que l'utilisateur ne l'a pas explicitement demandé. C'est
    /// l'application concrète de la règle « pas de capture par défaut ».
    /// </summary>
    public bool ShowClipboard { get; set; }

    public bool ShowFileShelf { get; set; } = true;

    /// <summary>
    /// Suivi des téléchargements, d'après le dossier Téléchargements. Activé par
    /// défaut : il ne lit que des noms et des tailles de fichiers, jamais leur
    /// contenu.
    /// </summary>
    public bool ShowDownloads { get; set; } = true;

    /// <summary>
    /// Micro et caméra en cours d'utilisation, d'après l'indicateur de
    /// confidentialité de Windows. Un appel ou un enregistrement prend une bulle
    /// si une autre activité occupe la notch. Activé par défaut : l'information
    /// est déjà celle que Windows affiche, et elle ne quitte jamais la machine.
    /// </summary>
    public bool ShowPrivacy { get; set; } = true;

    /// <summary>Regroupe les activités d'arrière-plan au-delà de la première.</summary>
    public bool ShowActivityStack { get; set; } = true;


    /// <summary>
    /// Affiche l'heure dans la forme de veille. Désactivée par défaut : elle
    /// introduirait une horloge à la minute dans un produit dont la promesse est
    /// de ne rien faire au repos.
    /// </summary>
    public bool ShowClockAtRest { get; set; }

    public bool EnableDiagnostics { get; set; } = true;

    /// <summary>Recherche : cibles épinglées en favoris, dans l'ordre d'épinglage.</summary>
    public List<string> LauncherFavorites { get; set; } = [];

    /// <summary>Recherche : dernières cibles ouvertes, la plus récente d'abord.</summary>
    public List<string> LauncherRecents { get; set; } = [];

    /// <summary>Recherche : nombre d'ouvertures par cible, pour départager les résultats.</summary>
    public Dictionary<string, int> LauncherLaunches { get; set; } = [];

    /// <summary>
    /// Profondeur d'historique du presse-papier. Zéro désactive la capture, ce
    /// qui garantit qu'aucune donnée n'est conservée par défaut.
    /// </summary>
    public int ClipboardHistoryLimit { get; set; }

    /// <summary>
    /// Profondeur appliquée lorsqu'on active la surveillance sans en choisir une.
    ///
    /// Assez pour retrouver ce qu'on vient de copier, assez peu pour que
    /// l'historique ne devienne pas une archive de tout ce qui passe par le
    /// presse-papier d'une journée de travail.
    /// </summary>
    private const int DefaultClipboardHistoryLimit = 50;

    // ---- Activation des fonctionnalités ---------------------------------

    /// <summary>
    /// Préférence d'activation d'une fonctionnalité, lue par son identifiant.
    /// C'est le seul endroit où la correspondance entre une fonctionnalité et sa
    /// préférence est écrite : ajouter une fonctionnalité n'oblige donc pas à
    /// retrouver tous les points qui la mentionnent.
    /// </summary>
    /// <returns>
    /// La préférence enregistrée, ou <c>true</c> pour une fonctionnalité qui
    /// n'expose pas de bascule — mieux vaut l'activer que la rendre inatteignable.
    /// </returns>
    public bool IsFeatureEnabled(string featureId) => featureId switch
    {
        FeatureKeys.Media => ShowMedia,
        FeatureKeys.VolumeHud => ShowVolumeHud,
        FeatureKeys.Notifications => ShowNotifications,
        FeatureKeys.Bluetooth => ShowBluetooth,
        FeatureKeys.Clipboard => ShowClipboard,
        FeatureKeys.FileShelf => ShowFileShelf,
        FeatureKeys.Downloads => ShowDownloads,
        FeatureKeys.Privacy => ShowPrivacy,
        _ => true
    };

    /// <summary>
    /// Indique si une fonctionnalité dispose d'une préférence persistée.
    ///
    /// Sert à distinguer une fonctionnalité que l'utilisateur peut éteindre
    /// durablement d'une autre, sans réglage associé : présenter une bascule qui
    /// oublierait son état au redémarrage serait un mensonge d'interface.
    /// </summary>
    public static bool HasFeaturePreference(string featureId) => featureId switch
    {
        FeatureKeys.Media => true,
        FeatureKeys.VolumeHud => true,
        FeatureKeys.Notifications => true,
        FeatureKeys.Bluetooth => true,
        FeatureKeys.Clipboard => true,
        FeatureKeys.FileShelf => true,
        FeatureKeys.Downloads => true,
        FeatureKeys.Privacy => true,
        _ => false
    };

    /// <summary>
    /// Enregistre la préférence d'activation d'une fonctionnalité, afin qu'une
    /// bascule survive au redémarrage.
    /// </summary>
    /// <returns><c>false</c> si la fonctionnalité n'a pas de préférence associée.</returns>
    public bool BindFeature(string featureId, bool enabled)
    {
        switch (featureId)
        {
            case FeatureKeys.Media:
                ShowMedia = enabled;
                return true;

            case FeatureKeys.VolumeHud:
                ShowVolumeHud = enabled;
                return true;

            case FeatureKeys.Notifications:
                ShowNotifications = enabled;
                return true;

            case FeatureKeys.Bluetooth:
                ShowBluetooth = enabled;
                return true;

            case FeatureKeys.Clipboard:
                ShowClipboard = enabled;
                return true;

            case FeatureKeys.FileShelf:
                ShowFileShelf = enabled;
                return true;

            case FeatureKeys.Downloads:
                ShowDownloads = enabled;
                return true;

            case FeatureKeys.Privacy:
                ShowPrivacy = enabled;
                return true;

            default:
                return false;
        }
    }

    // ---- Migration --------------------------------------------------------

    /// <summary>
    /// Reprend une configuration écrite par une version antérieure.
    ///
    /// <para>
    /// La migration est <em>conditionnelle</em> : elle ne s'applique que si les
    /// anciennes valeurs diffèrent de leurs valeurs d'origine. Une configuration
    /// jamais touchée sur ces champs est donc indiscernable d'une configuration
    /// neuve, ce qui est exactement le comportement voulu — et une configuration
    /// où l'utilisateur avait réglé raideur et amortissement voit son mouvement
    /// conservé, traduit dans le vocabulaire perceptif.
    /// </para>
    ///
    /// <para>
    /// Les anciens rayons ne sont <em>pas</em> repris, délibérément : 26 DIP
    /// décrivaient une capsule de 34 de haut, et les reporter sur un panneau de 52
    /// donnerait une forme que personne n'a choisie. Le nouveau rayon de référence
    /// s'applique.
    /// </para>
    ///
    /// <para>Idempotente : les anciens champs sont remis à leur valeur d'origine
    /// après migration, donc un second appel ne fait rien.</para>
    /// </summary>
    public void Migrate()
    {
        bool legacyMotion = SpringStiffness != LegacySpringStiffness
            || SpringDamping != LegacySpringDamping
            || SpringMass != LegacySpringMass;

        if (legacyMotion)
        {
            SpringParameters migrated = SpringParameters.FromSettings(SpringStiffness, SpringDamping, SpringMass);

            SpringResponseSeconds = migrated.ResponseSeconds;
            SpringBounce = migrated.DampingRatio;
        }

        SpringStiffness = LegacySpringStiffness;
        SpringDamping = LegacySpringDamping;
        SpringMass = LegacySpringMass;
    }

    /// <summary>
    /// Vérifie la cohérence après chargement : une configuration corrompue ou
    /// éditée à la main ne doit pas pouvoir placer l'Island hors de l'écran ni la
    /// faire disparaître.
    /// </summary>
    public void Sanitize()
    {
        LauncherFavorites ??= [];
        LauncherRecents ??= [];
        LauncherLaunches ??= [];

        if (!Enum.IsDefined(Density))
        {
            Density = IslandContentDensity.Comfortable;
        }

        CornerRadiusBottom = Clamp(CornerRadiusBottom, 8, 40, NotchGeometry.DefaultCompactRadius);
        CornerRadiusExpanded = Clamp(CornerRadiusExpanded, 12, 48, NotchGeometry.DefaultExpandedRadius);
        ShoulderRadius = Clamp(ShoulderRadius, 0, 20, NotchGeometry.DefaultShoulder);

        if (!Enum.IsDefined(MotionStyle))
        {
            MotionStyle = MotionStyle.Natural;
        }

        // Une profondeur nulle signifie « ne rien conserver » : activer la
        // surveillance du presse-papier sans profondeur donnerait une bascule qui
        // n'enregistre rien, c'est-à-dire une fonctionnalité qui a l'air cassée.
        if (ShowClipboard && ClipboardHistoryLimit <= 0)
        {
            ClipboardHistoryLimit = DefaultClipboardHistoryLimit;
        }

        if (!ShowClipboard)
        {
            ClipboardHistoryLimit = 0;
        }
        CornerSmoothing = Clamp(CornerSmoothing, IslandShape.Circular, 4, IslandShape.Squircle);
        SpringResponseSeconds = Clamp(SpringResponseSeconds, 0.18, 1.20, 0.46);
        SpringBounce = Clamp(SpringBounce, 0.05, 1.20, 0.58);

        // Un préréglage n'est vrai que si la vitesse et le rebond sont les
        // siens : une configuration réglée à la main avant l'existence des
        // préréglages — ou éditée depuis — se déclare personnalisée plutôt que
        // d'afficher « Naturel » sur un mouvement qui ne l'est pas.
        if (MotionStyle != MotionStyle.Custom)
        {
            SpringParameters preset = MotionPresets.Spring(MotionStyle);

            if (Math.Abs(preset.ResponseSeconds - SpringResponseSeconds) > 0.005
                || Math.Abs(preset.DampingRatio - SpringBounce) > 0.005)
            {
                MotionStyle = MotionStyle.Custom;
            }
        }
        SpringMass = Clamp(SpringMass, 0.2, 4, 1.0);
        HorizontalOffset = Clamp(HorizontalOffset, -2000, 2000, 0);

        // Règle n°1 : la notch est collée au bord supérieur. Une valeur héritée,
        // ou éditée à la main, est ramenée à zéro plutôt que bornée.
        TopOffset = 0;
        ClipboardHistoryLimit = (int)Clamp(ClipboardHistoryLimit, 0, 500, 0);

        if (!Enum.IsDefined(DockEdge) || (!AllowSideEdges && DockEdge != NotchEdge.Top))
        {
            DockEdge = NotchEdge.Top;
        }

        DockOffset = Clamp(DockOffset, 0, 1, 0.5);

        if (!Enum.IsDefined(DetachFeel))
        {
            DetachFeel = DetachFeel.Natural;
        }

        StretchAmount = Clamp(StretchAmount, 0, 0.10, FluidMotion.MaximumStretch);
        TearDistance = Clamp(TearDistance, Detachment.MinimumTearDistance, Detachment.MaximumTearDistance, Detachment.TearDistance);

        if (!Enum.IsDefined(SurfaceTint))
        {
            SurfaceTint = SurfaceTint.Oled;
        }

        if (!TryParseColor(CustomSurfaceColor, out _))
        {
            CustomSurfaceColor = "#14161C";
        }

        SurfaceOpacity = Clamp(SurfaceOpacity, 0.55, 1, 1);
        SideShoulderRadius = Clamp(SideShoulderRadius, 0, 16, SideTab.DefaultShoulder);
        FloatingRadius = Clamp(FloatingRadius, 12, 48, NotchGeometry.DefaultExpandedRadius);
        FloatingShadowOpacity = Clamp(FloatingShadowOpacity, 0, 0.6, 0.30);
        OutlineOpacity = Clamp(OutlineOpacity, 0.05, 0.5, 0.14);

        if (!Enum.IsDefined(BubbleSize))
        {
            BubbleSize = ElementSize.Normal;
        }

        if (!Enum.IsDefined(TabSize))
        {
            TabSize = ElementSize.Normal;
        }
    }

    private static double Clamp(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    // ---- Champs hérités ---------------------------------------------------

    /// <summary>
    /// Champs de l'ancienne configuration, conservés uniquement pour pouvoir la
    /// relire. Aucun code de rendu ne doit les consulter : les utiliser
    /// reviendrait à laisser deux sources de vérité pour le même mouvement.
    ///
    /// Ils restent publics, et non internes, pour une raison précise : le
    /// générateur de sérialisation ne projette que les membres publics, donc des
    /// champs internes ne seraient jamais relus depuis le fichier et la migration
    /// n'aurait rien à traduire.
    /// </summary>
    public double SpringStiffness { get; set; } = LegacySpringStiffness;

    public double SpringDamping { get; set; } = LegacySpringDamping;

    public double SpringMass { get; set; } = LegacySpringMass;

    private const double LegacySpringStiffness = 220.0;

    private const double LegacySpringDamping = 22.0;

    private const double LegacySpringMass = 1.0;
}
