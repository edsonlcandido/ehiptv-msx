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

    [JsonPropertyName("headline")]
    public string? Headline { get; set; }

    [JsonPropertyName("hint")]
    public string? Hint { get; set; }

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

    [JsonPropertyName("imageFiller")]
    public string? ImageFiller { get; set; }

    [JsonPropertyName("imageWidth")]
    public double? ImageWidth { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }
}

/// <summary>Content Item Object: um item (cartao, linha, poster, botao).</summary>
public sealed class MsxContentItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("layout")]
    public string? Layout { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("titleHeader")]
    public string? TitleHeader { get; set; }

    [JsonPropertyName("titleFooter")]
    public string? TitleFooter { get; set; }

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
