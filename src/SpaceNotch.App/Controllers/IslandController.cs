using System;
using System.Collections.Generic;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch_App.Animations;

namespace SpaceNotch_App.Controllers;

/// <summary>
/// Chef d'orchestre de l'Island : traduit les activités en encombrements et fait
/// converger la machine d'état.
///
/// Deux responsabilités, volontairement sans recouvrement avec la fenêtre : la
/// fenêtre ne décide jamais de sa taille ni de sa forme — elle applique
/// l'encombrement reçu, qui provient de la scène déclarée par la fonctionnalité.
/// </summary>
public sealed class IslandController : IDisposable
{
    private readonly IslandStateManager _stateManager;
    private readonly ActivityManager _activityManager;
    private readonly IslandSpringAnimator _animator;
    private readonly Func<bool> _useSpringAnimations;
    private SpringParameters _motionParameters;
    private SpringParameters _hoverParameters;
    private IslandFootprint _collapsedFootprint;

    private IslandActivity? _presented;
    private Action<Action>? _post;

    /// <summary>
    /// Vrai lorsqu'une activité attend la fermeture pour être présentée. Voir
    /// <see cref="ActivityInterruption.Queue"/>.
    /// </summary>
    private bool _hasQueued;

    /// <summary>Vrai pendant un parcours de pile demandé par l'utilisateur.</summary>
    private bool _userDriven;

    /// <summary>Encombrement imposé pendant le survol d'un fichier, sinon <c>null</c>.</summary>
    private IslandFootprint? _dragTarget;
    private bool _disposed;

    /// <summary>
    /// Activités qui se sont déjà ouvertes d'elles-mêmes.
    ///
    /// Sert à ce qu'une republication mette à jour la carte sans réclamer
    /// l'attention une seconde fois. Voir <see cref="ReactToActivityChanged"/>.
    /// </summary>
    private readonly HashSet<string> _announcedActivities = new(StringComparer.Ordinal);

    /// <summary>Horodatage du dernier signalement de panne de géométrie.</summary>
    private long _lastGeometryFailure;

    public IslandController(
        IslandStateManager stateManager,
        ActivityManager activityManager,
        SpringParameters springParameters,
        SpringParameters hoverParameters,
        IslandFootprint collapsedFootprint,
        Func<bool> useSpringAnimations,
        Action<IslandFootprint> onFootprintChanged)
    {
        ArgumentNullException.ThrowIfNull(onFootprintChanged);

        _stateManager = stateManager;
        _activityManager = activityManager;
        _collapsedFootprint = collapsedFootprint;
        _useSpringAnimations = useSpringAnimations;
        _motionParameters = springParameters;
        _hoverParameters = hoverParameters;

        _animator = new IslandSpringAnimator(
            springParameters,
            footprint =>
            {
                // L'application d'une géométrie est appelée depuis la boucle de
                // rendu du compositeur, où une exception est avalée : la forme se
                // fige au milieu de son mouvement, la consommation s'envole d'une
                // exception par image, et rien nulle part ne le dit. C'est
                // exactement le symptôme qu'on a mis une journée à interpréter.
                // Une panne ici est donc signalée, une fois par seconde au plus —
                // assez pour être vue, assez peu pour ne pas noyer le journal.
                try
                {
                    onFootprintChanged(footprint);
                }
                catch (Exception ex)
                {
                    ReportGeometryFailure(ex);
                }

                FootprintChanged?.Invoke(this, footprint);
            },
            Settle);

        _stateManager.StateChanged += (_, e) => StateChanged?.Invoke(this, e.NewState);
        _activityManager.ActiveActivityChanged += OnActiveActivityChanged;
        _activityManager.ActivityRemoved += OnActivityRemoved;

        // État initial : aucune animation, l'Island démarre dans sa forme au repos.
        _animator.SnapTo(collapsedFootprint);
    }

    /// <summary>
    /// Signale une panne survenue en appliquant une géométrie, sans jamais en
    /// noyer le journal ni interrompre le mouvement.
    /// </summary>
    private void ReportGeometryFailure(Exception exception)
    {
        long now = Environment.TickCount64;

        if (now - _lastGeometryFailure < 1000)
        {
            return;
        }

        _lastGeometryFailure = now;

        SpaceNotch.Infrastructure.Logging.MiniLogger.Log(
            "[GEOMETRIE] l'application de la forme a échoué ; la fenêtre garde sa taille précédente",
            exception);
    }

    /// <summary>
    /// Fournit le moyen d'atteindre le fil d'interface, et devient obligatoire
    /// dès qu'une publication peut venir d'ailleurs.
    ///
    /// <para>
    /// Réagir à une activité peut animer, et animer touche le compositeur ainsi
    /// que le minuteur de file d'attente — deux objets qui n'existent que sur le
    /// fil d'interface. Or une publication ne vient pas forcément de ce fil : un
    /// greffon qui attend une réponse réseau publie depuis le pool, ce qui est
    /// légitime et documenté.
    /// </para>
    ///
    /// <para>
    /// La fenêtre passe ici son propre rétablissement de fil, qui exécute
    /// immédiatement lorsque l'appel est déjà sur le bon fil : rien ne change
    /// pour les chemins internes, qui représentent la quasi-totalité des cas.
    /// </para>
    /// </summary>
    public void SetDispatcher(Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(post);

        _post = post;
    }

    public IslandState State => _stateManager.CurrentState;

    public IslandFootprint CurrentFootprint => _animator.Current;

    public IslandFootprint CollapsedFootprint => _collapsedFootprint;

    public IslandActivity? PresentedActivity => _presented;

    /// <summary>Vrai lorsqu'un rendu par image est en cours.</summary>
    public bool IsAnimating => _animator.IsRunning;

    /// <summary>Images rendues depuis le démarrage, utilisé par les diagnostics.</summary>
    public long RenderedFrames => _animator.RenderedFrames;

    /// <summary>
    /// Activité à présenter. La fenêtre s'y abonne pour résoudre la scène par sa
    /// clé déclarée.
    /// </summary>
    public event EventHandler<IslandActivity?>? PresentedActivityChanged;

    public event EventHandler<IslandFootprint>? FootprintChanged;

    public event EventHandler<IslandState>? StateChanged;

    public event EventHandler? AnimationCompleted;

    /// <summary>
    /// Encombrement de l'aperçu au survol.
    ///
    /// Fourni par la fenêtre, et non calculé ici : le survol ne fait pas
    /// grandir l'Island d'un pourcentage, il la fait monter d'un palier de
    /// présentation — et la notion de palier appartient au rendu, pas à la
    /// mécanique du ressort.
    /// </summary>
    public Func<IslandFootprint>? PreviewFootprint { get; set; }

    /// <summary>
    /// Applique un nouveau réglage de ressort, sans recréer l'animateur : la
    /// fenêtre de réglages doit pouvoir ajuster la courbe en direct, et une
    /// réinitialisation ferait perdre la position et la vitesse courantes.
    ///
    /// Deux lois pour un même geste : celle de l'ouverture, autoritaire, et
    /// celle du survol, plus élastique. C'est la seconde qui porte le rebond
    /// visible, l'Island n'ayant qu'un bord libre. Voir ADR-012.
    /// </summary>
    public void UpdateSpringParameters(SpringParameters motion, SpringParameters hover)
    {
        _motionParameters = motion;
        _hoverParameters = hover;

        if (State == IslandState.Preview)
        {
            _animator.UpdateParameters(hover);
            return;
        }

        _animator.UpdateParameters(motion);
    }

    /// <summary>
    /// Change l'encombrement au repos. Une Island déjà ouverte n'est pas
    /// redimensionnée : sa taille appartient à la scène présentée, et la préférence
    /// ne concerne que la forme au repos.
    /// </summary>
    public void UpdateCollapsedFootprint(IslandFootprint footprint)
    {
        // **Un encombrement identique ne relance rien.**
        //
        // Sans ce retour, la boucle était refermée : le rendu réapplique
        // l'encombrement au repos, l'animation part, se termine, signale sa fin,
        // ce qui redemande un rendu — qui réapplique le même encombrement. Mesuré
        // à 500 tours par seconde : l'Island vibrait entre 34 et 151 DIP, donc
        // paraissait figée sur une taille intermédiaire, ne se repliait jamais, et
        // consommait 55 % d'un cœur au repos apparent.
        //
        // La comparaison est donc la garde, et non une optimisation : c'est elle
        // qui distingue « la préférence a changé » de « on me redemande la même
        // chose ».
        if (footprint == _collapsedFootprint)
        {
            return;
        }

        // La préférence est mémorisée dans tous les cas, y compris lorsque
        // l'Island est ouverte : elle s'appliquera à la fermeture.
        _collapsedFootprint = footprint;

        // Un fichier survole la notch : la cible de dépôt garde la forme, et le
        // repos s'appliquera quand le fichier partira.
        if (_dragTarget is not null)
        {
            return;
        }

        if (State is IslandState.Closed or IslandState.Preview)
        {
            // Une forme au repos qui change est un changement d'objet, pas un
            // effleurement : elle se pose avec la loi de l'ouverture.
            _animator.UpdateParameters(_motionParameters);
            AnimateTo(footprint);
        }
    }

    /// <summary>Réaction à un clic sur l'Island.</summary>
    public void ToggleFromUser()
    {
        if (State is IslandState.Expanded or IslandState.Expanding)
        {
            RequestCollapse();
        }
        else
        {
            RequestExpand();
        }
    }

    /// <summary>
    /// Ouvre l'Island sur la scène de l'activité présentée. Sans activité, il n'y
    /// a rien à ouvrir : c'est la scène qui fournit l'encombrement.
    /// </summary>
    public void RequestExpand()
    {
        if (_presented is null)
        {
            return;
        }

        _animator.UpdateParameters(_motionParameters);
        _stateManager.TryTransitionTo(IslandState.Expanding);
        AnimateTo(_presented.Footprint);
    }

    public void RequestCollapse()
    {
        _animator.UpdateParameters(_motionParameters);
        _stateManager.TryTransitionTo(IslandState.Collapsing);
        AnimateTo(_collapsedFootprint);
    }

    /// <summary>
    /// Aperçu au survol : l'Island monte d'un palier de présentation.
    ///
    /// Le survol ne l'ouvre pas et ne l'agrandit pas d'un pourcentage — il
    /// annonce le palier suivant, puis se retire. C'est ce qui le rend
    /// prévisible : on sait toujours ce qu'un effleurement va montrer, même
    /// lorsque la souris ne fait que traverser le haut de l'écran par accident.
    /// </summary>
    public void RequestPreview()
    {
        if (PreviewFootprint is null || State is IslandState.Expanded or IslandState.Expanding)
        {
            return;
        }

        // Depuis un aperçu déjà en cours, on se contente de réorienter la cible :
        // une activité peut apparaître pendant que le pointeur est posé, et le
        // palier à annoncer change alors sans que l'utilisateur ait bougé.
        if (State != IslandState.Preview && !_stateManager.TryTransitionTo(IslandState.Preview))
        {
            return;
        }

        _animator.UpdateParameters(_hoverParameters);
        AnimateTo(PreviewFootprint());
    }

    public void EndPreview()
    {
        if (State != IslandState.Preview)
        {
            return;
        }

        if (_stateManager.TryTransitionTo(IslandState.Closed))
        {
            // Le retour au repos quitte la loi du survol : sinon la forme
            // retomberait avec un rebond d'effleurement, ce qui se lirait comme
            // une hésitation.
            _animator.UpdateParameters(_motionParameters);
            AnimateTo(_collapsedFootprint);
        }
    }

    /// <summary>
    /// Parcourt la pile d'activités à la demande de l'utilisateur — molette,
    /// flèches, satellite.
    ///
    /// Passer par ici plutôt que par le gestionnaire directement n'est pas un
    /// détour : un changement demandé par l'utilisateur ne doit jamais être mis
    /// en attente comme une arrivée spontanée, même notch ouverte.
    /// </summary>
    public bool CyclePresentation(int delta)
    {
        _userDriven = true;

        try
        {
            return _activityManager.CyclePresentation(delta);
        }
        finally
        {
            _userDriven = false;
        }
    }

    /// <summary>
    /// Un fichier survole la notch : elle devient une cible visuelle, sans
    /// changer d'état. Le ressort d'effleurement porte ce mouvement — c'est une
    /// invitation, pas une ouverture.
    /// </summary>
    public void BeginDragTarget(IslandFootprint footprint)
    {
        if (_dragTarget == footprint)
        {
            return;
        }

        _dragTarget = footprint;
        _animator.UpdateParameters(_hoverParameters);
        AnimateTo(footprint);
    }

    /// <summary>
    /// Le fichier est parti, ou a été déposé : la notch reprend la forme de son
    /// état.
    /// </summary>
    public void EndDragTarget()
    {
        if (_dragTarget is null)
        {
            return;
        }

        _dragTarget = null;
        _animator.UpdateParameters(_motionParameters);
        AnimateTo(FootprintForState());
    }

    /// <summary>Encombrement que l'état courant réclame.</summary>
    private IslandFootprint FootprintForState() => State switch
    {
        IslandState.Expanded or IslandState.Expanding when _presented is not null => _presented.Footprint,
        IslandState.Preview when PreviewFootprint is not null => PreviewFootprint(),
        _ => _collapsedFootprint
    };

    private void OnActiveActivityChanged(object? sender, IslandActivity? activity)
    {
        // Le changement de fil précède la réaction, et non l'inverse : animer
        // depuis un fil de travail ferait échouer le mouvement, pas seulement le
        // décaler.
        Post(() => ReactToActivityChanged(activity));
    }

    /// <summary>
    /// Réaction à un changement d'activité, sur le fil d'interface.
    /// </summary>
    private void ReactToActivityChanged(IslandActivity? activity)
    {
        NotchPresentation presentation = NotchPresentationResolver.Resolve(State, _presented);
        ActivityInterruption decision = ActivityPolicies.Decide(_presented, activity, presentation);

        // L'utilisateur a ouvert la notch pour regarder quelque chose : une
        // arrivée moins urgente attend la fermeture au lieu de remplacer le
        // contenu sous son pointeur. Trois conditions, toutes nécessaires : ce
        // n'est pas lui qui parcourt la pile, ce qu'il regarde existe encore, et
        // la règle de cohabitation demande l'attente.
        if (decision == ActivityInterruption.Queue
            && !_userDriven
            && _presented is not null
            && StillActive(_presented.Id))
        {
            _hasQueued = true;
            return;
        }

        _hasQueued = false;
        _presented = activity;
        PresentedActivityChanged?.Invoke(this, activity);

        if (activity is null)
        {
            if (State != IslandState.Closed)
            {
                RequestCollapse();
            }

            return;
        }

        // Une activité prioritaire s'ouvre d'elle-même — mais **une seule fois
        // par activité**, et non à chaque republication.
        //
        // La distinction n'est pas cosmétique : une activité qui rafraîchit son
        // contenu — une météo, un téléchargement — republie sous le même
        // identifiant. Sans cette mémoire, chaque rafraîchissement rouvrirait
        // l'Island que l'utilisateur vient de replier. Le geste de fermeture
        // serait alors sans effet, indéfiniment, ce qui se lit comme une panne.
        bool claimsAttention = decision == ActivityInterruption.Interrupt
            || activity.Priority >= ActivityPriority.High;

        if (claimsAttention && _announcedActivities.Add(activity.Id))
        {
            RequestExpand();
            return;
        }

        if (State is IslandState.Expanded or IslandState.Expanding)
        {
            AnimateTo(activity.Footprint);
        }
    }

    private bool StillActive(string activityId)
    {
        foreach (IslandActivity candidate in _activityManager.GetActiveActivities())
        {
            if (string.Equals(candidate.Id, activityId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void OnActivityRemoved(object? sender, IslandActivity activity)
    {
        // Une activité retirée oublie qu'elle s'est annoncée : si elle revient,
        // elle est nouvelle, et elle mérite de nouveau l'attention.
        _announcedActivities.Remove(activity.Id);

        Post(RearmExpirationWindow);
    }

    /// <summary>
    /// Exécute une action sur le fil d'interface. Sans répartiteur fourni, elle
    /// s'exécute sur place : c'est le comportement des tests et d'un hôte qui
    /// n'a pas d'interface à protéger.
    /// </summary>
    private void Post(Action action)
    {
        if (_post is null)
        {
            action();
            return;
        }

        _post(action);
    }

    /// <summary>
    /// Résout l'encombrement en cours. Le ressort n'est utilisé que si Windows
    /// autorise les animations ; sinon la géométrie est appliquée directement,
    /// ce qui reste perceptible mais évite tout mouvement.
    /// </summary>
    private void AnimateTo(IslandFootprint target)
    {
        bool spring = _useSpringAnimations();

        if (spring)
        {
            _animator.AnimateTo(target);
            return;
        }

        _animator.SnapTo(target);
        Settle();
    }

    /// <summary>
    /// Amène la machine d'état à un état stable. Sans cette étape, l'Island
    /// resterait bloquée en <c>Expanding</c> et les transitions suivantes
    /// seraient refusées.
    /// </summary>
    private void Settle()
    {
        switch (State)
        {
            case IslandState.Expanding:
                _stateManager.TryTransitionTo(IslandState.Expanded);
                break;

            case IslandState.Collapsing:
                _stateManager.TryTransitionTo(IslandState.Closed);
                break;
        }

        // La notch vient de se refermer sur une activité qui attendait : elle est
        // présentée maintenant, dans la forme au repos.
        if (_hasQueued && State == IslandState.Closed)
        {
            _hasQueued = false;
            ReactToActivityChanged(_activityManager.CurrentActivity);
        }

        AnimationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void RearmExpirationWindow() => ExpirationWindowChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Signalé lorsque l'ensemble des échéances a changé : l'hôte en profite pour
    /// réarmer son unique minuteur d'expiration.
    /// </summary>
    public event EventHandler? ExpirationWindowChanged;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activityManager.ActiveActivityChanged -= OnActiveActivityChanged;
        _activityManager.ActivityRemoved -= OnActivityRemoved;
        _animator.Stop();
        GC.SuppressFinalize(this);
    }
}
