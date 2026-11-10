using System.Text.Json;

namespace ehiptv_msx.Services;

/// <summary>Opcoes de configuracao do app (appsettings.json).</summary>
public sealed class XtreamOptions
{
    /// <summary>Base do Xtream Codes (ex.: http://<provedor>).</summary>
    public string BaseUrl { get; set; } = "http://<provedor>";

    /// <summary>
    /// URL publica do proprio app (ex.: https://ehiptv.ehtudo.app). Necesario
    /// em producao porque o proxy pode nao repassar o Host original.
    /// Vazio = usa scheme+host da requisicao (dev local).
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Categoria usada na tela inicial de Filmes.</summary>
    public string DefaultVodCategory { get; set; } = "25";

    /// <summary>Categoria usada na tela inicial de Series.</summary>
    public string DefaultSeriesCategory { get; set; } = "3178";

    /// <summary>Itens por pagina na grade de posteres (12 = 6 colunas x 2 linhas).</summary>
    public int PosterItemsPerPage { get; set; } = 12;

    /// <summary>
    /// Limite de itens de uma grade de posteres. O MSX aceita no maximo 56
    /// paginas no modo "pages" (56 x 12 = 672 itens).
    /// </summary>
    public int MaxPosterItems { get; set; } = 672;

    /// <summary>Limite de canais exibidos em uma categoria.</summary>
    public int MaxChannelItems { get; set; } = 1200;

    /// <summary>Usuario opcional (fora do repo: vem de secrets/env).</summary>
    public string? Username { get; set; }

    /// <summary>Senha opcional (fora do repo: vem de secrets/env).</summary>
    public string? Password { get; set; }
}

/// <summary>Credencial do Xtream Codes em memoria (nunca vai para o repo).</summary>
public sealed record XtreamCredential(string BaseUrl, string Username, string Password)
{
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    /// <summary>Segmento de path URL-encoded: "usuario/senha".</summary>
    public string PathSegment =>
        $"{Uri.EscapeDataString(Username)}/{Uri.EscapeDataString(Password)}";

    /// <summary>Versao com a base, para montar as URLs de playback.</summary>
    public string Base(string kind, string id, string? extension = null) =>
        $"{BaseUrl.TrimEnd('/')}/{kind}/{Uri.EscapeDataString(Username)}/" +
        $"{Uri.EscapeDataString(Password)}/{id}{(extension is null ? string.Empty : "." + extension)}";
}

/// <summary>
/// Resolve a credencial do Xtream Codes sem que o usuario precise digitar
/// usuario e senha em lugar nenhum:
///
///  1. path  : /msx/{usuario}/{senha}/... (forma gerada pelo proprio app)
///  2. query : ?u=usuario&amp;p=senha
///  3. host  : http://usuario-senha.localhost:5000/... (localhost)
///  4. secret: arquivo local de secrets (apenas dev / servidor proprio)
///
/// A ordem importa: o path e o query explicitos vencem a inferencia do host.
/// </summary>
public sealed class CredentialResolver
{
    private readonly XtreamOptions _options;
    private readonly XtreamCredential? _fallback;

    public CredentialResolver(Microsoft.Extensions.Options.IOptions<XtreamOptions> options)
    {
        _options = options.Value;

        var fromSecrets = SecretsLoader.TryLoad();
        var user = string.IsNullOrWhiteSpace(_options.Username) ? fromSecrets?.Username : _options.Username;
        var pass = string.IsNullOrWhiteSpace(_options.Password) ? fromSecrets?.Password : _options.Password;

        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
        {
            _fallback = new XtreamCredential(_options.BaseUrl, user!, pass!);
        }
    }

    /// <summary>Credencial vinda do path da rota (usuario/senha), se houver.</summary>
    public XtreamCredential Resolve(HttpContext context, string? routeUser = null, string? routePass = null)
    {
        if (!string.IsNullOrWhiteSpace(routeUser) && !string.IsNullOrWhiteSpace(routePass))
        {
            return new XtreamCredential(_options.BaseUrl, routeUser, routePass);
        }

        var query = context.Request.Query;
        var user = query["u"].FirstOrDefault() ?? query["username"].FirstOrDefault();
        var pass = query["p"].FirstOrDefault() ?? query["password"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
        {
            return new XtreamCredential(_options.BaseUrl, user!, pass!);
        }

        var fromHost = FromHost(context.Request.Host.Host);
        if (fromHost is not null)
        {
            return fromHost;
        }

        return _fallback ?? new XtreamCredential(_options.BaseUrl, string.Empty, string.Empty);
    }

    /// <summary>Extrai "usuario-senha" do primeiro label do host (localhost).</summary>
    private XtreamCredential? FromHost(string host)
    {
        var prefix = CredentialPrefix(host);
        if (prefix is null)
        {
            return null;
        }

        var separator = prefix.IndexOf('-');
        return new XtreamCredential(_options.BaseUrl, prefix[..separator], prefix[(separator + 1)..]);
    }

    /// <summary>
    /// Prefixo de credencial do host ("<usuario>-<senha>" em
    /// "<usuario>-<senha>.localhost"). Ambos os lados precisam ter ao menos 3
    /// caracteres, para nao confundir um host comum com barra ("app-01").
    /// </summary>
    public static string? CredentialPrefix(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var label = host.Split('.', 2)[0];
        var separator = label.IndexOf('-');

        if (separator < 3 || label.Length - separator - 1 < 3)
        {
            return null;
        }

        return label;
    }
}

/// <summary>
/// Le o arquivo local de secrets (fora do repositorio) com as credenciais de
/// fallback. Caminho: variavel de ambiente EHIPTV_MSX_SECRETS_FILE ou, no
/// Windows, %USERPROFILE%\.minimax\secrets\ehiptv-msx.json
/// </summary>
public static class SecretsLoader
{
    public const string EnvVar = "EHIPTV_MSX_SECRETS_FILE";

    public static XtreamCredential? TryLoad()
    {
        var path = Environment.GetEnvironmentVariable(EnvVar);

        if (string.IsNullOrWhiteSpace(path))
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(profile, ".minimax", "secrets", "ehiptv-msx.json");
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            if (root.TryGetProperty("xtream", out var xtream))
            {
                root = xtream;
            }

            var baseUrl = GetString(root, "baseUrl") ?? "http://<provedor>";
            var user = GetString(root, "username");
            var pass = GetString(root, "password");

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                return null;
            }

            return new XtreamCredential(baseUrl, user!, pass!);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[secrets] arquivo '{path}' ignorado: {ex.Message}");
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
