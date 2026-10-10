using System;
using SpaceNotch.Core.Channel;

namespace SpaceNotch.Core.Motion;

/// <summary>Humeur d'un agent, telle que Pixel la montre (ADR-029).</summary>
public enum AgentMood
{
    /// <summary>L'agent travaille : le regard cherche en haut, trois points avancent.</summary>
    Thinking = 0,

    /// <summary>L'agent demande une autorisation : yeux ronds vers toi, « ? » bleu qui clignote.</summary>
    Asking = 1,

    /// <summary>Terminé : yeux rieurs, un petit saut, des étincelles vertes.</summary>
    Done = 2,

    /// <summary>Erreur, ou réponse attendue dans le terminal : yeux inquiets, il secoue la tête, « ! » rouge.</summary>
    Error = 3
}

/// <summary>Le petit signe à droite des yeux.</summary>
public enum AvatarSign
{
    /// <summary>Trois points : <see cref="AvatarPose.SignStep"/> dit combien sont allumés (1 à 3).</summary>
    Dots,

    /// <summary>« ? » bleu.</summary>
    Question,

    /// <summary>Deux étincelles vertes : <see cref="AvatarPose.SignStep"/> dit laquelle brille (0 ou 1).</summary>
    Sparkle,

    /// <summary>« ! » rouge.</summary>
    Exclamation
}

/// <summary>
/// Une image de Pixel en avatar, en DIP à l'échelle 1. Les deux yeux ont la
/// même forme ; <see cref="Tilt"/> penche l'œil gauche d'autant et le droit à
/// l'inverse (coins intérieurs relevés : inquiet). <see cref="Arcs"/> dessine
/// des yeux rieurs (« ^ ») au lieu de pavés.
/// </summary>
public readonly record struct AvatarPose(
    EyeShape Eye,
    bool Arcs,
    double LookX,
    double LookY,
    double Tilt,
    double Hop,
    double Shake,
    AvatarSign Sign,
    int SignStep,
    double SignOpacity);

/// <summary>
/// Pixel, avatar des agents (ADR-029) : les mêmes yeux qu'au repos, qui
/// réfléchissent, demandent, fêtent la fin ou s'inquiètent, avec un petit signe
/// pour que l'état se lise sans la couleur. Remplace Clawd.
///
/// <para>
/// Tout est ici, sans Windows : la vue ne fait que dessiner la pose d'un
/// instant. Quand Windows réduit les animations, la pose est fixe et choisie
/// pour être lisible.
/// </para>
/// </summary>
public static class PixelAvatar
{
    /// <summary>Largeur d'un œil au repos, en DIP.</summary>
    public const double EyeWidth = 8;

    /// <summary>Écart entre les deux yeux, en DIP (le même qu'au repos).</summary>
    public const double EyeGap = 7;

    /// <summary>Espace entre l'œil droit et le signe, en DIP.</summary>
    public const double SignGap = 4;

    /// <summary>Largeur réservée au signe, en DIP.</summary>
    public const double SignWidth = 9;

    /// <summary>Largeur de l'avatar, signe compris, en DIP.</summary>
    public const double Width = EyeWidth + EyeGap + EyeWidth + SignGap + SignWidth;

    /// <summary>Hauteur de l'avatar : un œil rond, et la place du saut, en DIP.</summary>
    public const double Height = 14;

    /// <summary>Cadence du dessin : assez pour un regard souple, peu pour le processeur.</summary>
    public const int FramesPerSecond = 15;

    /// <summary>Centre de l'œil gauche, au repos, en DIP depuis le coin haut-gauche.</summary>
    public const double LeftEyeX = EyeWidth / 2;

    /// <summary>Centre de l'œil droit, au repos, en DIP depuis le coin haut-gauche.</summary>
    public const double RightEyeX = EyeWidth + EyeGap + (EyeWidth / 2);

    /// <summary>Hauteur du centre des yeux, en DIP depuis le haut.</summary>
    public const double EyeY = Height / 2;

    /// <summary>Durée d'un aller-retour du regard qui cherche.</summary>
    private const double ScanSeconds = 3.2;

    /// <summary>Rythme du « ? » et des petits bonds de la demande.</summary>
    private const double AskSeconds = 1.2;

    /// <summary>Le double saut de la fin, puis il reste posé.</summary>
    private const double CelebrateSeconds = 0.9;

    /// <summary>Il secoue la tête, puis s'arrête, et recommence.</summary>
    private const double ShakeSeconds = 0.8;

    private const double ShakeEverySeconds = 2.6;

    /// <summary>Yeux rieurs : larges et bas, tracés en « ^ ».</summary>
    public static EyeShape Laughing { get; } = new(10, 5, 0.2);

    /// <summary>Yeux inquiets : aplatis, et penchés de <see cref="WorryTilt"/>.</summary>
    public static EyeShape Worried { get; } = new(8, 5, 0.2);

    /// <summary>Inclinaison des yeux inquiets, en degrés.</summary>
    public const double WorryTilt = 14;

    /// <summary>Le signe qui dit l'humeur sans la couleur.</summary>
    public static AvatarSign SignOf(AgentMood mood) => mood switch
    {
        AgentMood.Asking => AvatarSign.Question,
        AgentMood.Done => AvatarSign.Sparkle,
        AgentMood.Error => AvatarSign.Exclamation,
        _ => AvatarSign.Dots
    };

    /// <summary>
    /// L'humeur d'un agent. Une attente sans question — Claude Code attend une
    /// saisie dans le terminal — se montre comme une erreur : il faut y aller.
    /// </summary>
    public static AgentMood MoodOf(ChannelState state, bool asks) => state switch
    {
        ChannelState.Waiting when asks => AgentMood.Asking,
        ChannelState.Waiting => AgentMood.Error,
        ChannelState.Done => AgentMood.Done,
        ChannelState.Error => AgentMood.Error,
        _ => AgentMood.Thinking
    };

    /// <summary>
    /// La pose de Pixel <paramref name="seconds"/> après le début de l'humeur.
    /// Sans <paramref name="animate"/>, une pose fixe, lisible : aucun
    /// mouvement, le signe pleinement visible.
    /// </summary>
    public static AvatarPose Pose(AgentMood mood, double seconds, bool animate = true)
    {
        double t = animate && double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;

        return mood switch
        {
            AgentMood.Asking => Asking(t, animate),
            AgentMood.Done => Done(t, animate),
            AgentMood.Error => Error(t, animate),
            _ => Thinking(t, animate)
        };
    }

    private static AvatarPose Thinking(double t, bool animate)
    {
        // Le regard va et vient en haut, comme quand on cherche une idée ; les
        // points s'allument un à un. Posé : il regarde en haut à gauche, trois points.
        double look = animate ? Math.Sin(2 * Math.PI * t / ScanSeconds) : -0.6;
        int dots = animate ? 1 + ((int)(t * 2.5) % 3) : 3;

        return new AvatarPose(PixelGaze.Shape(PixelMood.Awake), false, PixelGaze.MaxLookX * look, -PixelGaze.MaxLookY, 0, 0, 0, AvatarSign.Dots, dots, 1);
    }

    private static AvatarPose Asking(double t, bool animate)
    {
        // Yeux ronds, droit vers toi ; un petit bond au début de chaque battement,
        // le « ? » vif puis estompé. Posé : ni bond, « ? » plein.
        double phase = (t % AskSeconds) / AskSeconds;
        double hop = animate && phase < 0.25 ? -2 * Math.Sin(Math.PI * phase / 0.25) : 0;
        double opacity = !animate || phase < 0.55 ? 1 : 0.35;

        return new AvatarPose(PixelGaze.Shape(PixelMood.Surprised), false, 0, PixelGaze.MaxLookY / 2, 0, hop, 0, AvatarSign.Question, 0, opacity);
    }

    private static AvatarPose Done(double t, bool animate)
    {
        // Deux petits sauts, puis il reste content ; les étincelles scintillent.
        double hop = animate && t < CelebrateSeconds ? -3 * Math.Abs(Math.Sin(Math.PI * t / (CelebrateSeconds / 2))) : 0;
        int sparkle = animate ? (int)(t * 4) % 2 : 0;

        return new AvatarPose(Laughing, true, 0, 0, 0, hop, 0, AvatarSign.Sparkle, sparkle, 1);
    }

    private static AvatarPose Error(double t, bool animate)
    {
        // Il secoue la tête, de moins en moins fort, puis s'arrête, et recommence.
        double cycle = t % ShakeEverySeconds;
        double shake = animate && cycle < ShakeSeconds
            ? 1.5 * Math.Sin(2 * Math.PI * cycle * 5) * (1 - (cycle / ShakeSeconds))
            : 0;

        return new AvatarPose(Worried, false, 0, PixelGaze.MaxLookY / 2, WorryTilt, 0, shake, AvatarSign.Exclamation, 0, 1);
    }
}
