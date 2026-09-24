using System;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace NotchFlow_App.Composition;

/// <summary>
/// Ombre portée de l'Island, calculée par le compositeur.
///
/// <para>
/// <b>Pourquoi une ombre, et pourquoi pas seulement elle.</b> Une ombre portée ne
/// se distingue pas d'un fond d'écran déjà sombre : sur un papier peint noir, elle
/// disparaît, et un objet qui ne compterait que sur elle s'effacerait selon le
/// choix de l'utilisateur. C'est la règle des thèmes sombres — l'élévation s'y
/// exprime par la surface, l'ombre vient en accompagnement — et sa conséquence ici
/// est que le reflet spéculaire de l'Island n'est pas une décoration mais le
/// second porteur de la séparation. Voir ADR-013.
/// </para>
///
/// <para>
/// <b>Pourquoi une silhouette plutôt qu'une boîte.</b> L'ombre est masquée par la
/// forme réelle, et non par un rectangle : elle épouse donc les congés, et elle
/// suit le morphing sans qu'on ait à l'animer séparément. Une ombre animée à part
/// finirait toujours par se désynchroniser de la forme qu'elle est censée décrire,
/// et c'est exactement ce qu'on remarque sans savoir le nommer.
/// </para>
///
/// <para>
/// <b>Pourquoi le porteur est transparent.</b> Le visuel qui porte l'ombre est
/// peint d'une couleur entièrement transparente : seule son ombre est visible. Un
/// porteur opaque dessinerait un aplat clair sous une surface translucide, et il
/// apparaîtrait à travers elle — l'Island aurait un cœur blanc.
/// </para>
/// </summary>
public sealed class IslandShadowSurface : IDisposable
{
    /// <summary>Opacité au repos : l'objet est posé près de l'écran.</summary>
    private const float RestOpacity = 0.30f;

    /// <summary>Opacité déployée : l'objet se détache, son ombre s'étale et pâlit.</summary>
    private const float DeployedOpacity = 0.12f;

    private const float RestBlurRadius = 24f;

    private const float DeployedBlurRadius = 34f;

    private const float RestOffsetY = 8f;

    private const float DeployedOffsetY = 11f;

    private readonly SpriteVisual _caster;
    private readonly DropShadow _shadow;
    private readonly CompositionRoundedRectangleGeometry _geometry;
    private readonly ShapeVisual _shape;
    private readonly CompositionVisualSurface _surface;
    private readonly CompositionSurfaceBrush _mask;

    private double _width = -1;
    private double _height = -1;
    private double _radius = -1;
    private double _shoulder = -1;

    private bool _disposed;

    private IslandShadowSurface(Compositor compositor, UIElement host)
    {
        _geometry = compositor.CreateRoundedRectangleGeometry();
        _geometry.Size = Vector2.Zero;
        _geometry.CornerRadius = Vector2.Zero;

        // La forme source : elle n'est jamais affichée telle quelle, elle sert de
        // masque à l'ombre.
        _shape = compositor.CreateShapeVisual();

        var spriteShape = compositor.CreateSpriteShape(_geometry);
        spriteShape.FillBrush = compositor.CreateColorBrush(Colors.White);
        _shape.Shapes.Add(spriteShape);

        // Capture en surface : c'est ce qui rend la forme acceptable comme masque
        // par l'API de composition, qui n'accepte pas une géométrie directement.
        _surface = compositor.CreateVisualSurface();
        _surface.SourceVisual = _shape;

        _mask = compositor.CreateSurfaceBrush(_surface);
        _mask.Stretch = CompositionStretch.Fill;

        _caster = compositor.CreateSpriteVisual();

        // Entièrement transparent : le porteur ne doit rien peindre, seule son
        // ombre compte.
        _caster.Brush = compositor.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));

        _shadow = compositor.CreateDropShadow();
        _shadow.Mask = _mask;
        _shadow.BlurRadius = RestBlurRadius;
        _shadow.Offset = new Vector3(0, RestOffsetY, 0);
        _shadow.Color = Colors.Black;
        _shadow.Opacity = RestOpacity;

        _caster.Shadow = _shadow;

        ElementCompositionPreview.SetElementChildVisual(host, _caster);

        IsAvailable = true;
    }

    /// <summary>Vrai lorsque le compositeur a accepté la surface.</summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// Tente d'attacher une ombre à un élément hôte. Renvoie <c>null</c> si le
    /// compositeur refuse : une décoration ne doit jamais empêcher l'Island de
    /// s'afficher.
    /// </summary>
    public static IslandShadowSurface? TryAttach(UIElement host)
    {
        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
            return new IslandShadowSurface(compositor, host);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Aligne la silhouette de l'ombre sur celle de la notch.
    ///
    /// <para>
    /// <b>Le haut de l'ombre est plat, comme celui de la notch.</b> Un rectangle
    /// arrondi arrondit ses quatre coins ; la notch n'arrondit que les deux du
    /// bas. Le rectangle est donc prolongé d'un rayon au-dessus du bord et
    /// décalé d'autant vers le haut : ses coins supérieurs tombent hors de la
    /// surface capturée, et l'ombre part du bord de l'écran exactement comme la
    /// forme qu'elle accompagne. Sans cela, l'ombre dessinerait deux coins
    /// arrondis sous un bord droit — la silhouette d'une capsule sous celle
    /// d'une notch.
    /// </para>
    ///
    /// <para>
    /// Aucun travail n'est refait tant que la forme n'a pas changé : le ressort
    /// produit des dizaines d'images presque identiques en fin de course, et
    /// réécrire la géométrie pour elles n'aurait aucun effet visible.
    /// </para>
    /// </summary>
    /// <param name="width">Largeur de la notch, épaules comprises, en DIPs.</param>
    /// <param name="height">Hauteur de la notch, en DIPs.</param>
    /// <param name="radius">Rayon des congés du bas effectivement tracé, en DIPs.</param>
    /// <param name="shoulder">Épaule effectivement tracée, en DIPs : l'ombre suit le corps, pas les épaules.</param>
    public void Configure(double width, double height, double radius, double shoulder = 0)
    {
        if (_disposed)
        {
            return;
        }

        if (Math.Abs(width - _width) < 0.05
            && Math.Abs(height - _height) < 0.05
            && Math.Abs(radius - _radius) < 0.05
            && Math.Abs(shoulder - _shoulder) < 0.05)
        {
            return;
        }

        _width = width;
        _height = height;
        _radius = radius;
        _shoulder = shoulder;

        var size = new Vector2((float)width, (float)height);
        float body = (float)Math.Max(0, width - (2 * shoulder));
        float r = (float)Math.Max(0, radius);

        _geometry.Offset = new Vector2((float)shoulder, -r);
        _geometry.Size = new Vector2(body, (float)height + r);
        _geometry.CornerRadius = new Vector2(r, r);
        _shape.Size = size;
        _surface.SourceSize = size;
        _caster.Size = size;
    }

    /// <summary>
    /// Fait glisser l'ombre d'un état à l'autre, sans mécanisme d'animation.
    ///
    /// L'avancement est fourni par la géométrie, pas par une machine d'état : la
    /// forme se déploie et l'ombre suit le chemin en même temps, faute de quoi un
    /// basculement discret au mauvais instant se verrait comme un clignotement.
    /// </summary>
    /// <param name="deployment">0 au repos, 1 complètement déployé.</param>
    public void SetDeployment(double deployment)
    {
        if (_disposed)
        {
            return;
        }

        float progress = (float)Math.Clamp(deployment, 0.0, 1.0);

        _shadow.Opacity = Lerp(RestOpacity, DeployedOpacity, progress);
        _shadow.BlurRadius = Lerp(RestBlurRadius, DeployedBlurRadius, progress);
        _shadow.Offset = new Vector3(0, Lerp(RestOffsetY, DeployedOffsetY, progress), 0);
    }

    private static float Lerp(float from, float to, float progress)
        => from + ((to - from) * progress);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _caster.Shadow = null;
        _shadow.Dispose();
        _mask.Dispose();
        _surface.Dispose();
        _shape.Dispose();
        _geometry.Dispose();
        _caster.Dispose();
    }
}
