using System.Text.Json;

namespace ehiptv_msx.Services;

/// <summary>Opcoes de configuracao do app (appsettings.json).</summary>
public sealed class XtreamOptions
{
    /// <summary>
    /// Base do Xtream Codes. Vem de fora do repo: variavel de ambiente
    /// "BASE_URL" (ou "Xtream__BaseUrl") ou do arquivo de secrets.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Bases alternativas do Xtream Codes, tentadas em ordem quando a principal
    /// falha. Alguns provedores bloqueiam o IP do servidor (a resposta é 404):
    /// com um segundo dominio do mesmo provedor o app volta a funcionar.
    /// Vem da variavel de ambiente "FALLBACK_URL" (separada por virgula).
    /// </summary>
    public string[] FallbackUrls { get; set; } = [];

    /// <summary>
    /// Relay (proxy) opcional para o player_api.php. Quando preenchido, o app
    /// chama "{ApiProxyUrl}/player_api.php?username=..&password=..&action=.."
    /// e espera o JSON do Xtream Codes de volta. Use quando nenhum dominio do
    /// provedor é alcançavel a partir do servidor.
    /// </summary>
    public string? ApiProxyUrl { get; set; }

    /// <summary>
    /// URL publica do proprio app (ex.: https://ehiptv.ehtudo.app). Necesario
    /// em producao porque o proxy pode nao repassar o Host original.
    /// Vazio = usa scheme+host da requisicao (dev local).
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Esquema das URLs geradas ("http" ou "https"). Padrao "http": o
    /// provedor so serve HTTP, e um app em HTTPS carregando conteudo HTTP
    /// cai no bloqueio de mixed content do navegador.
    /// Vazio = usa o esquema da requisicao.
    /// </summary>
    public string? PublicScheme { get; set; } = "http";

    /// <summary>Categoria usada na tela inicial de Filmes.</summary>
    public string DefaultVodCategory { get; set; } = "25";

    /// <summary>Categoria usada na tela inicial de Series.</summary>
    public string DefaultSeriesCategory { get; set; } = "3178";

    /// <summary>
    /// Categoria usada na tela inicial de Canais ("Canais | Abertos" no
    /// provedor). Se o id nao existir, a primeira categoria e usada.
    /// </summary>
    public string DefaultLiveCategory { get; set; } = "1";

    /// <summary>
    /// Limite de itens de uma tela de Filmes/Séries. Cada página do navegador
    /// tem 10 cartazes, então 672 itens = 68 páginas (o MSX aceita no máximo
    /// 1024 páginas no tipo "list" e 256 itens por página).
    /// </summary>
    public int MaxPosterItems { get; set; } = 672;

    /// <summary>Limite de canais exibidos em uma categoria.</summary>
    public int MaxChannelItems { get; set; } = 1200;

    /// <summary>
    /// Deriva a base das URLs de playback do "server_info" devolvido pelo
    /// player_api.php (padrao IPTV Xtream Codes). Desligue com
    /// Xtream__UseServerInfo=false para usar sempre o host do painel.
    /// </summary>
    public bool UseServerInfo { get; set; } = true;

    /// <summary>
    /// Base fixa das URLs de playback. Vazio = usa o "server_info" do
    /// player_api.php.
    /// </summary>
    public string? PlaybackBaseUrl { get; set; }

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

    /// <summary>
    /// URL de playback no padrao IPTV Xtream Codes
    /// ("{base}/{live|movie|series}/{usuario}/{senha}/{id}.{ext}") com uma base
    /// especifica (por exemplo, o "server_info" do player_api.php).
    /// </summary>
    public string BuildFrom(string baseUrl, string kind, string id, string? extension = null) =>
        $"{baseUrl.TrimEnd('/')}/{kind}/{Uri.EscapeDataString(Username)}/" +
        $"{Uri.EscapeDataString(Password)}/{id}{(extension is null ? string.Empty : "." + extension)}";
}

/// <summary>
/// Resolve a credencial do Xtream Codes sem que o usuario precise digitar
/// usuario e senha em lugar nenhum:
///
///  1. host  : http://usuario-senha.host/... (primeiro trecho da URL)
///  2. query : ?u=usuario&amp;p=senha
///  3. secret: arquivo local de secrets (apenas dev / servidor proprio)
///
/// A credencial fica SEMPRE no primeiro trecho do host: nao existe mais
/// sub-caminho com usuario/senha nas URLs.
/// </summary>
public sealed class CredentialResolver
{
    private readonly XtreamOptions _options;
    private readonly XtreamCredential? _fallback;

    public CredentialResolver(Microsoft.Extensions.Options.IOptions<XtreamOptions> options)
    {
        _options = options.Value;

        // As opcoes ja vem mescladas com o ambiente e com o arquivo de
        // secrets (ver Program.cs): aqui so falta montar o fallback local.
        var user = _options.Username;
        var pass = _options.Password;

        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
        {
            _fallback = new XtreamCredential(_options.BaseUrl, user!, pass!);
        }
    }

    /// <summary>Credencial da requisicao (host -&gt; query -&gt; secrets).</summary>
    public XtreamCredential Resolve(HttpContext context)
    {
        var fromHost = FromHost(PublicHost(context));
        if (fromHost is not null)
        {
            return fromHost;
        }

        var query = context.Request.Query;
        var user = query["u"].FirstOrDefault() ?? query["username"].FirstOrDefault();
        var pass = query["p"].FirstOrDefault() ?? query["password"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
        {
            return new XtreamCredential(_options.BaseUrl, user!, pass!);
        }

        return _fallback ?? new XtreamCredential(_options.BaseUrl, string.Empty, string.Empty);
    }

    /// <summary>
    /// Host visto pelo app. Atras de um proxy o "Host" chega interno
    /// (ex.: localhost:3000); o host publico vem no X-Forwarded-Host.
    /// </summary>
    public static string PublicHost(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return context.Request.Host.Value ?? context.Request.Host.Host;
    }

    /// <summary>
    /// Scheme publico. A configuracao "PublicScheme" manda (o padrao e "http",
    /// porque o provedor so serve HTTP e HTTPS no app daria mixed content);
    /// depois vem o X-Forwarded-Proto e, por ultimo, o esquema da requisicao.
    /// </summary>
    public static string PublicScheme(HttpContext context, string? forced = null)
    {
        if (!string.IsNullOrWhiteSpace(forced))
        {
            return forced.Trim();
        }

        var forwarded = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return context.Request.Scheme;
    }

    /// <summary>Extrai "usuario-senha" do primeiro label do host.</summary>
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
/// Secrets do Xtream Codes lidos de um arquivo local (fora do repositorio).
/// </summary>
public sealed record XtreamSecrets(string BaseUrl, string Username, string Password, string[] FallbackUrls);

/// <summary>
/// Le o arquivo local de secrets (fora do repositorio) com as credenciais e as
/// bases do provedor. Caminho: variavel de ambiente EHIPTV_MSX_SECRETS_FILE ou,
/// no Windows, %USERPROFILE%\.minimax\secrets\ehiptv-msx.json
/// </summary>
public static class SecretsLoader
{
    public const string EnvVar = "EHIPTV_MSX_SECRETS_FILE";

    public static XtreamSecrets? TryLoad()
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

            var baseUrl = GetString(root, "baseUrl") ?? string.Empty;
            var user = GetString(root, "username");
            var pass = GetString(root, "password");
            var fallbacks = GetStringArray(root, "fallbackUrls");

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                return null;
            }

            return new XtreamSecrets(baseUrl, user!, pass!, fallbacks);
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

    private static string[] GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();
    }
}
