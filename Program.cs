using System.Net;
using System.Text;
using System.Text.Json;
using ehiptv_msx.Models;
using ehiptv_msx.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();

builder.Services.AddHttpClient("xtream", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ehiptv-msx/1.0)");
});

builder.Services.Configure<XtreamOptions>(builder.Configuration.GetSection("Xtream"));
builder.Services.AddSingleton<CredentialResolver>();
builder.Services.AddSingleton<XtreamClient>();
builder.Services.AddSingleton<MsxContentFactory>();

// CORS: o MSX busca o start.json/menu.json com withCredentials=true.
// Em requisicao com credencial o wildcard "*" e invalido (o navegador bloqueia
// a resposta e o XHR falha com "Server responded with status: 0"), por isso a
// origem tem de ser ecoada de volta, com Access-Control-Allow-Credentials.
builder.Services.AddCors(options =>
{
    options.AddPolicy("MsxClients", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowCredentials()
              .WithHeaders("Origin", "Content-Type", "Accept")
              .WithMethods("GET", "OPTIONS");
    });
});

var app = builder.Build();

app.UseCors("MsxClients");

// ---------------------------------------------------------------------------
// Rotas
// ---------------------------------------------------------------------------

app.MapGet("/", () => Results.Redirect("/msx/start.json"));

app.MapGet("/health", () => Results.Json(new { ok = true }));

// Start Object: MSX sempre busca em {SERVER}/msx/start.json.
// A credencial vem da URL (path, query ou host usuario-senha.localhost).
app.MapGet("/msx/start.json", (HttpContext context, CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);

    if (!credential.HasCredentials)
    {
        return Results.Json(new MsxStart
        {
            Name = "Eh! IPTV",
            Version = "1.0.0",
            Parameter = $"content:{HelpUrl(context)}"
        }, MsxJson.Options);
    }

    return Results.Json(factory.BuildStart(context, credential), MsxJson.Options);
});

// Ajuda: como montar a URL com usuario e senha.
app.MapGet("/msx/ajuda.json", (HttpContext context) =>
    Results.Json(BuildHelpContent(context), MsxJson.Options));

var scoped = app.MapGroup("/msx/{user}/{pass}");

scoped.MapGet("/menu.json", async (HttpContext context, string user, string pass,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context, user, pass);
    var menu = await factory.BuildMenuAsync(context, credential);
    return Results.Json(menu, MsxJson.Options);
});

scoped.MapGet("/content/home.json", (HttpContext context, string user, string pass,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context, user, pass);
    return Results.Json(factory.BuildHome(context, credential), MsxJson.Options);
});

// Canais: hub de categorias.
scoped.MapGet("/content/live.json", async (HttpContext context, string user, string pass,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context, user, pass);
    var content = await factory.BuildLiveHubAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

// Canais: lista de canais da categoria.
scoped.MapGet("/content/live/{categoryId}.json", async (HttpContext context, string user, string pass,
    string categoryId, CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context, user, pass);
    var content = await factory.BuildLiveCategoryAsync(context, credential, categoryId);
    return Results.Json(content, MsxJson.Options);
});

// Filmes: hub (categoria padrao) e categoria especifica.
scoped.MapGet("/content/vod.json", async (HttpContext context, string user, string pass,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context, user, pass);
    var content = await factory.BuildVodHubAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

scoped.MapGet("/content/vod/{categoryId}.json", async (HttpContext context, string user, string pass,
    string categoryId, CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context, user, pass);
    var content = await factory.BuildVodCategoryAsync(context, credential, categoryId);
    return Results.Json(content, MsxJson.Options);
});

// Series: hub, categoria e episodios.
scoped.MapGet("/content/series.json", async (HttpContext context, string user, string pass,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context, user, pass);
    var content = await factory.BuildSeriesHubAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

scoped.MapGet("/content/series/{categoryId}.json", async (HttpContext context, string user, string pass,
    string categoryId, CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context, user, pass);
    var content = await factory.BuildSeriesCategoryAsync(context, credential, categoryId);
    return Results.Json(content, MsxJson.Options);
});

scoped.MapGet("/content/series-info/{seriesId}.json", async (HttpContext context, string user, string pass,
    string seriesId, CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context, user, pass);
    var content = await factory.BuildSeriesInfoAsync(context, credential, seriesId);
    return Results.Json(content, MsxJson.Options);
});

// ---------------------------------------------------------------------------
// Preview no site do MSX (base64, sem servidor)
// ---------------------------------------------------------------------------

// Menu compacto em JSON (util para gerar o link base64 na mao).
app.MapGet("/msx/preview.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var menu = await factory.BuildPreviewMenuAsync(context, credential);
    return Results.Json(menu, MsxJson.Options);
});

// Pagina de preview: abre o msx.benzac.de com o JSON embutido em base64.
app.MapGet("/msx/preview", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var menu = await factory.BuildPreviewMenuAsync(context, credential);
    var json = JsonSerializer.Serialize(menu, MsxJson.Options);
    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    var testUrl = "https://msx.benzac.de/?start=menu:json:" + Uri.EscapeDataString(base64);

    var html = $$"""
<!doctype html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Eh! IPTV - Preview MSX</title>
<style>
  :root { color-scheme: dark; }
  body { margin:0; background:#111; color:#eee; font:14px/1.4 system-ui, sans-serif; }
  header { display:flex; gap:16px; align-items:center; padding:10px 16px; background:#1c1c1c; border-bottom:1px solid #2e2e2e; }
  h1 { font-size:15px; margin:0; font-weight:600; }
  a { color:#7cc4ff; }
  code { color:#9aa; }
  iframe { width:100%; height:calc(100vh - 46px); border:0; display:block; }
</style>
</head>
<body>
<header>
  <h1>Eh! IPTV &mdash; preview do Media Station X</h1>
  <span>payload: <code>{{json.Length:N0}} bytes JSON / {{base64.Length:N0}} base64</code></span>
  <a href="{{WebUtility.HtmlEncode(testUrl)}}" target="_blank" rel="noopener">abrir em nova aba</a>
</header>
<iframe src="{{WebUtility.HtmlEncode(testUrl)}}" allow="autoplay; fullscreen"></iframe>
</body>
</html>
""";

    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static string HelpUrl(HttpContext context) =>
    $"{context.Request.Scheme}://{context.Request.Host}/msx/ajuda.json";

static MsxContent BuildHelpContent(HttpContext context) => new()
{
    Type = "list",
    Headline = "Como usar",
    Items =
    [
        new MsxContentItem
        {
            Title = "URL com usuario e senha no host (localhost)",
            Text = $"http://{context.Request.Host.Host.Split('.', 2)[0]}-<senha>.localhost:{context.Request.Host.Port}/msx/start.json"
        },
        new MsxContentItem
        {
            Title = "URL com usuario e senha na query",
            Text = $"http://{context.Request.Host}/msx/start.json?u=<usuario>&p=<senha>"
        },
        new MsxContentItem
        {
            Title = "No app MSX",
            Text = "Settings > Start Parameter > Setup > informe o dominio do servidor"
        }
    ]
};
