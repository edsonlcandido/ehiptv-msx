using System.Text.Json;
using System.Text.Json.Serialization;

namespace ehiptv_msx.Models;

/// <summary>
/// Start Object do MSX (endpoint "/msx/start.json").
/// O "parameter" aponta para o Menu Root Object e carrega as credenciais do
/// Xtream Codes na URL, para que o app MSX consiga buscar tudo sem digitar
/// usuario e senha.
/// </summary>
public sealed class MsxStart
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Eh! IPTV";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("parameter")]
    public string Parameter { get; set; } = string.Empty;
}

/// <summary>Menu Root Object do MSX (a coluna da esquerda).</summary>
public sealed class MsxMenu
{
    [JsonPropertyName("headline")]
    public string Headline { get; set; } = "Eh! IPTV";

    [JsonPropertyName("menu")]
    public List<MsxMenuItem> Menu { get; set; } = [];
}

/// <summary>
/// Menu Item Object. O "data" aceita uma URL (string) ou um Content Root
/// Object embutido (objeto), conforme a documentacao do MSX.
/// </summary>
public sealed class MsxMenuItem
{
    /// <summary>"default" | "separator" | "settings".</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>URL do Content Root Object ou o proprio Content Root Object.</summary>
    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

/// <summary>Content Root Object do MSX (a area da direita).</summary>
public sealed class MsxContent
{
    /// <summary>"pages" (grade/horizontal) ou "list" (lista vertical).</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "pages";

    /// <summary>
    /// Comprime a grade de 12x6 para 16x8 (25% menor por celula). Necessario
    /// para as telas de Filmes e Series, onde a barra lateral de categorias
    /// divide a tela com a grade de cartazes.
    /// </summary>
    [JsonPropertyName("compress")]
    public bool? Compress { get; set; }

    [JsonPropertyName("headline")]
    public string? Headline { get; set; }

    [JsonPropertyName("hint")]
    public string? Hint { get; set; }

    /// <summary>
    /// Texto pequeno no cabecalho da tela (ex.: "Total: 500"). No Paging
    /// Plugin do MSX e onde o total de itens aparece.
    /// </summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    [JsonPropertyName("template")]
    public MsxTemplate? Template { get; set; }

    [JsonPropertyName("items")]
    public List<MsxContentItem>? Items { get; set; }

    [JsonPropertyName("pages")]
    public List<MsxContentPage>? Pages { get; set; }
}

/// <summary>Content Page Object: uma "tela" dentro de um Content Root.</summary>
public sealed class MsxContentPage
{
    [JsonPropertyName("items")]
    public List<MsxContentItem> Items { get; set; } = [];
}

/// <summary>Template visual aplicado a todos os itens de um Content Root.</summary>
public sealed class MsxTemplate
{
    /// <summary>"default" | "separate" | "teaser" | "button" | "space" | "control".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "separate";

    [JsonPropertyName("layout")]
    public string? Layout { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("iconSize")]
    public string? IconSize { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Cabecalho do titulo (placeholder do programa no ar).</summary>
    [JsonPropertyName("titleHeader")]
    public string? TitleHeader { get; set; }

    /// <summary>Rodape do titulo (placeholder do horario do programa).</summary>
    [JsonPropertyName("titleFooter")]
    public string? TitleFooter { get; set; }

    [JsonPropertyName("imageFiller")]
    public string? ImageFiller { get; set; }

    [JsonPropertyName("imageWidth")]
    public double? ImageWidth { get; set; }

    /// <summary>-1 = barra de progresso escondida, mas atualizavel.</summary>
    [JsonPropertyName("progress")]
    public double? Progress { get; set; }

    /// <summary>Propriedades estendidas (ex.: "progress:type": "fix:Live").</summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, string>? Properties { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }
}

/// <summary>Content Item Object: um item (cartao, linha, poster, botao).</summary>
public sealed class MsxContentItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Foca este item ao abrir a tela. Sem isso o MSX foca o primeiro item
    /// (a barra lateral de categorias) em vez da grade de cartazes.
    /// </summary>
    [JsonPropertyName("focus")]
    public bool? Focus { get; set; }

    /// <summary>
    /// Numeracao do item no cabecalho ("Canais (3/71)"). Usado nas listas de
    /// canais ao vivo, no estilo do demo "Channel List" do MSX.
    /// </summary>
    [JsonPropertyName("enumerate")]
    public bool? Enumerate { get; set; }

    /// <summary>
    /// Propriedades estendidas do MSX (ex.: "progress:type": "fix:Live",
    /// que faz a barra de progresso do item ser a do programa no ar).
    /// </summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, string>? Properties { get; set; }

    /// <summary>
    /// Live Object do MSX: atualiza o item periodicamente. Com
    /// type "setup", a acao roda uma vez quando o item fica visivel — e e
    /// assim que o app busca o programa atual do canal no servidor.
    /// </summary>
    [JsonPropertyName("live")]
    public MsxLiveObject? Live { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("layout")]
    public string? Layout { get; set; }

    /// <summary>
    /// Ajuste fino da posicao do item ("x,y,w,h", valores relativos). E o
    /// que da o espacamento de 1,5 linha por canal na lista de TV.
    /// </summary>
    [JsonPropertyName("offset")]
    public string? Offset { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("titleHeader")]
    public string? TitleHeader { get; set; }

    [JsonPropertyName("titleFooter")]
    public string? TitleFooter { get; set; }

    /// <summary>
    /// Barra de progresso do item. -1 = escondida, mas atualizavel (e o que
    /// o MSX preenche com o programa no ar).
    /// </summary>
    [JsonPropertyName("progress")]
    public double? Progress { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("headline")]
    public string? Headline { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("iconSize")]
    public string? IconSize { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("badge")]
    public string? Badge { get; set; }

    [JsonPropertyName("badgeColor")]
    public string? BadgeColor { get; set; }

    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    [JsonPropertyName("tagColor")]
    public string? TagColor { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("imageFiller")]
    public string? ImageFiller { get; set; }

    [JsonPropertyName("imageWidth")]
    public double? ImageWidth { get; set; }

    [JsonPropertyName("imageHeight")]
    public double? ImageHeight { get; set; }

    [JsonPropertyName("playerLabel")]
    public string? PlayerLabel { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public static class MsxJson
{
    /// <summary>
    /// Opcoes de serializacao do MSX: nomes explicitos via JsonPropertyName,
    /// nulls omitidos (o MSX nao deve receber chaves vazias) e sem indentacao
    /// (o payload vai dentro de uma URL base64).
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

/// <summary>
/// Live Object do MSX (Experts API): permite atualizar o item periodicamente
/// (programa no ar, progresso da barra, relogio...).
/// </summary>
public sealed class MsxLiveObject
{
    /// <summary>"setup" | "schedule" | "lifetime" | "airtime" | "playback".</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Inicio do programa (epoch em milissegundos).</summary>
    [JsonPropertyName("from")]
    public long? From { get; set; }

    /// <summary>Fim do programa (epoch em milissegundos).</summary>
    [JsonPropertyName("to")]
    public long? To { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("titleHeader")]
    public string? TitleHeader { get; set; }

    [JsonPropertyName("titleFooter")]
    public string? TitleFooter { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("progress")]
    public double? Progress { get; set; }

    /// <summary>Conteudo enquanto o programa esta no ar.</summary>
    [JsonPropertyName("running")]
    public MsxLiveState? Running { get; set; }

    /// <summary>Conteudo quando o programa termina (e a proxima chamada).</summary>
    [JsonPropertyName("over")]
    public MsxLiveState? Over { get; set; }

    /// <summary>Acao executada pelo MSX (ex.: "execute:service:{URL}").</summary>
    [JsonPropertyName("execute")]
    public MsxLiveState? Execute { get; set; }

    /// <summary>
    /// Acao do live object. Com type "setup" ela roda uma vez, quando o
    /// item fica visivel (e o que dispara a busca do programa no ar).
    /// </summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }
}

/// <summary>Estado do Live Object (conteudo e acao de um estado).</summary>
public sealed class MsxLiveState
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("titleHeader")]
    public string? TitleHeader { get; set; }

    [JsonPropertyName("titleFooter")]
    public string? TitleFooter { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("progress")]
    public double? Progress { get; set; }

    /// <summary>Acao executada pelo MSX neste estado.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }
}

/// <summary>
/// Resposta do "servico" de live data do MSX: o envelope
/// {"response":{"status":200,"text":"OK","data":{"action":"update:content:ID",
/// "data":{"live":{...}}}}}.
/// </summary>
public sealed class MsxLiveResponse
{
    [JsonPropertyName("response")]
    public MsxResponseEnvelope Response { get; set; } = new();
}

public sealed class MsxResponseEnvelope
{
    [JsonPropertyName("status")]
    public int Status { get; set; } = 200;

    [JsonPropertyName("text")]
    public string Text { get; set; } = "OK";

    /// <summary>Detalhe do erro (so em respostas de erro).</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Conteudo da resposta. No servico de live data e um
    /// <see cref="MsxResponseData"/> (com a acao "update:content:ID"); num
    /// content service (Paging Plugin) e o proprio Content Root.
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public sealed class MsxResponseData
{
    /// <summary>Acao aplicada pelo MSX (ex.: "update:content:ch_1234").</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("data")]
    public MsxLiveUpdate? Data { get; set; }
}

/// <summary>
/// Resposta de um "content service" do MSX (o que o Paging Plugin consome):
/// {"response":{"status":200,"text":"OK","data":{ ...content root... }}}.
/// </summary>
public sealed class MsxServiceResponse
{
    [JsonPropertyName("response")]
    public MsxResponseEnvelope Response { get; set; } = new();
}

public sealed class MsxLiveUpdate
{
    [JsonPropertyName("live")]
    public MsxLiveObject? Live { get; set; }
}
