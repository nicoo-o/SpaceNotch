using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Core.Animation;

/// <summary>
/// L'animateur de la forme, vu du contrôleur (phase E) : un ressort à deux
/// dimensions qui garde sa position et sa vitesse. L'application le branche
/// sur l'horloge d'images ; les tests le remplacent par un faux qui avance à
/// la demande. C'est ce qui permet au contrôleur de vivre en Core, testé.
/// </summary>
public interface IShapeAnimator
{
    /// <summary>Encombrement courant, à mi-parcours compris.</summary>
    IslandFootprint Current { get; }

    /// <summary>Vitesse verticale courante, en DIP/s (0 au repos).</summary>
    double HeightVelocity { get; }

    /// <summary>Vrai tant que le ressort bouge.</summary>
    bool IsRunning { get; }

    /// <summary>Images rendues depuis le démarrage (diagnostics).</summary>
    long RenderedFrames { get; }

    /// <summary>Plus grand intervalle observé entre deux images, en millisecondes.</summary>
    double MaxFrameGapMilliseconds { get; }

    /// <summary>Intervalles de plus de 25 ms.</summary>
    long LongFrameCount { get; }

    /// <summary>Cadence moyenne observée.</summary>
    double MeasuredFrameRate { get; }

    /// <summary>Change la loi du ressort sans interrompre le mouvement.</summary>
    void UpdateParameters(SpringParameters parameters);

    /// <summary>Oriente le ressort vers une cible en gardant position et vitesse.</summary>
    void AnimateTo(IslandFootprint target);

    /// <summary>Pose un encombrement sans animation.</summary>
    void SnapTo(IslandFootprint footprint);

    /// <summary>Fait partir le prochain mouvement d'une forme et d'une vitesse données.</summary>
    void Seed(IslandFootprint from, double heightVelocity);

    /// <summary>Arrête le ressort.</summary>
    void Stop();
}
