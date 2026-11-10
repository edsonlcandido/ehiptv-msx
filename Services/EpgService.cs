using System.Text;
using ehiptv_msx.Models;

namespace ehiptv_msx.Services;

/// <summary>
/// Servico de live data do MSX (Experts API).
///
/// Quando o app MSX mostra um canal, ele chama este endereco
/// ("execute:service:{URL}") e recebe de volta o programa que esta no ar,
/// no formato de atualizacao de item:
///
/// {"response":{"status":200,"text":"OK","data":{
///    "action":"update:content:ch_1234",
///    "data":{"live":{"type":"schedule","from":...,"to":...,
///            "titleHeader":"Nome do programa",
///            "titleFooter":"{from:time:hh:mm} - {to:time:hh:mm} (...)",
///            "running":{...},"over":{"action":"execute:service:..."}}}}}}
///
/// O "over" refaz a chamada quando o programa termina, entao a lista se
/// atualiza sozinha enquanto o canal fica visivel.
/// </summary>
public sealed class EpgService
{
    private readonly XtreamClient _xtream;

    public EpgService(XtreamClient xtream)
    {
        _xtream = xtream;
    }

    /// <summary>
    /// Monta a resposta de live data do canal: o programa atual, o horario
    /// e a barra de progresso (o MSX calcula a progresso a partir do
    /// "from"/"to").
    /// </summary>
    public async Task<MsxLiveResponse> BuildResponseAsync(
        XtreamCredential credential, string streamId, string itemId, string serviceUrl)
    {
        var live = await BuildLiveObjectAsync(credential, streamId, serviceUrl);

        return new MsxLiveResponse
        {
            Response = new MsxResponseEnvelope
            {
                Status = 200,
                Text = "OK",
                Data = new MsxResponseData
                {
                    Action = $"update:content:{itemId}",
                    Data = new MsxLiveUpdate { Live = live }
                }
            }
        };
    }

    /// <summary>Live Object com o programa atual do canal.</summary>
    public async Task<MsxLiveObject> BuildLiveObjectAsync(
        XtreamCredential credential, string streamId, string serviceUrl)
    {
        var listings = await _xtream.GetShortEpgAsync(credential, streamId, 2);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // O provedor ja devolve a lista comecando no programa atual; o filtro
        // cobre o caso de a resposta ter sido montada ha alguns minutos.
        var current = listings.FirstOrDefault(listing =>
                         listing.StartUnix <= now && listing.StopUnix > now)
                     ?? listings.FirstOrDefault(listing => listing.StartUnix > 0);

        if (current is null)
        {
            // Canal sem EPG no provedor: o item fica sem programa e sem
            // barra. O "over" refaz a consulta em uma hora, caso o provedor
            // publique a programacao mais tarde.
            return new MsxLiveObject
            {
                Type = "schedule",
                From = now * 1000,
                To = (now + 3600) * 1000,
                TitleHeader = string.Empty,
                TitleFooter = string.Empty,
                Over = new MsxLiveState
                {
                    Action = $"execute:service:{serviceUrl}"
                }
            };
        }

        return new MsxLiveObject
        {
            Type = "schedule",
            From = current.StartUnix * 1000,
            To = current.StopUnix * 1000,
            TitleHeader = Decode(current.Title),
            TitleFooter = "{from:time:hh:mm} - {to:time:hh:mm} ({duration:text:hm})",
            Running = new MsxLiveState
            {
                TitleFooter = "{from:time:hh:mm} - {to:time:hh:mm} ({duration:text:hm}) | {countdown:text:hm} left"
            },
            Over = new MsxLiveState
            {
                // Quando o programa termina, o MSX chama o servico de novo.
                Action = $"execute:service:{serviceUrl}"
            }
        };
    }

    /// <summary>
    /// O provedor manda o titulo e a descricao do programa em base64. Se o
    /// texto nao for base64 valido (ou virar lixo nao imprimivel), ele e
    /// usado como esta.
    /// </summary>
    public static string Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        if (text.Length % 4 != 0)
        {
            return text;
        }

        Span<byte> buffer = new byte[text.Length / 4 * 3];

        if (!Convert.TryFromBase64String(text, buffer, out var written) || written == 0)
        {
            return text;
        }

        var decoded = Encoding.UTF8.GetString(buffer[..written]);

        if (decoded.Any(character => char.IsControl(character) &&
                                     character is not '\n' and not '\r' and not '\t'))
        {
            return text;
        }

        return decoded;
    }
}
