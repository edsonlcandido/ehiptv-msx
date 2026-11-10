# Eh! IPTV no Media Station X

App MSX (Media Station X) que serve o catálogo do **Xtream Codes** como páginas JSON
do MSX. Roda como uma API ASP.NET: o app MSX busca o `start.json`, o `menu.json` e
as telas de conteúdo — tudo gerado a partir do provedor, em tempo real.

## As 4 telas

| # | Tela | Conteúdo |
|---|------|----------|
| 1 | **Início** | 3 cards: Canais ao vivo / Filmes e VOD / Séries e episódios |
| 2 | **Canais** | Lista de categorias de TV (linhas largas) → canais da categoria (logo + nome), que abrem o player HLS |
| 3 | **Filmes** | Grade de pôsteres com nota → reproduz o filme (`/movie/….mp4`) |
| 4 | **Séries** | Grade de pôsteres → página de episódios (`S01E01 - …`) → reproduz (`/series/….mp4`) |

O menu lateral (coluna da esquerda) traz `Início`, `Canais`, `Filmes`, `Séries` e todas as
categorias de cada seção.

## A credencial vai no início da URL

O usuário e a senha **sempre ficam no começo da URL** (prefixo do host) e valem para
todas as chamadas que o MSX faz:

```
http://<usuario>-<senha>.localhost:8080/msx/start.json
http://<usuario>-<senha>.seu-dominio.com/msx/start.json
```

O MSX sempre busca `{SERVER}/msx/start.json`; ao responder, o app devolve um Start Object
cujo `parameter` aponta para o menu **com o mesmo prefixo de host**, e todas as URLs geradas
(menu, telas, links de conteúdo) mantêm esse prefixo. Nenhum usuário/senha é digitado na
TV: basta configurar o domínio no app MSX.

Ordem de resolução da credencial (`Services/CredentialResolver.cs`):

1. **path** — `/msx/{usuario}/{senha}/…` (usado por todas as URLs geradas)
2. **query** — `?u=usuario&p=senha`
3. **host** — `usuario-senha.host` (o prefixo do início da URL)
4. **secret local** — arquivo de secrets (fallback de dev/servidor)

## Endpoints

| Rota | Descrição |
|------|-----------|
| `GET /msx/start.json` | Start Object (parâmetro já com a credencial) |
| `GET /msx/{u}/{p}/menu.json` | Menu Root (menu lateral) |
| `GET /msx/{u}/{p}/content/home.json` | Tela 1 — Início |
| `GET /msx/{u}/{p}/content/live.json` | Tela 2 — Canais (categorias) |
| `GET /msx/{u}/{p}/content/live/{categoryId}.json` | Canais da categoria |
| `GET /msx/{u}/{p}/content/vod.json` | Tela 3 — Filmes |
| `GET /msx/{u}/{p}/content/vod/{categoryId}.json` | Filmes da categoria |
| `GET /msx/{u}/{p}/content/series.json` | Tela 4 — Séries |
| `GET /msx/{u}/{p}/content/series/{categoryId}.json` | Séries da categoria |
| `GET /msx/{u}/{p}/content/series-info/{seriesId}.json` | Episódios da série |
| `GET /msx/preview` | Preview no `msx.benzac.de` com o JSON em base64 |
| `GET /msx/preview.json` | Menu compacto (JSON autocontido) |
| `GET /health` | Health check |

Todas as respostas JSON devolvem `Access-Control-Allow-Origin` echoing a origem +
`Access-Control-Allow-Credentials` (o MSX usa `withCredentials=true`).

## Instalar no app MSX (TV / desktop)

1. Publique o app (Docker/Easypanel) e aponte o domínio para ele.
2. Crie um registro DNS curinga: `*.seu-dominio.com` → mesmo IP/serviço
   (ex.: `<usuario>-<senha>.seu-dominio.com`).
3. No app MSX: **Settings → Start Parameter → Setup** → informe
   `<usuario>-<senha>.seu-dominio.com` (sem `http://`), o lock de segurança e confirme.
4. O app carrega o catálogo e o usuário navega pelas telas.

> Com wildcard DNS + TLS curinga, o prefixo de credencial funciona em qualquer rede.
> Sem wildcard, use o domínio fixo e passe a credencial via
> `?u=…&p=…` (útil para links diretos, não para o Setup do app).

## Testar sem servidor (site do MSX)

O MSX web aceita um JSON inteiro em base64 na URL:

```
https://msx.benzac.de/?start=menu:json:{BASE64}            # carrega direto
https://msx.benzac.de/info/?tab=Demo&start=menu:json:{BASE64}   # editor + botão Launch
```

Para gerar esse payload com dados reais, rode o app e pegue `/msx/preview.json`
(menu compacto, com o conteúdo embutido — não depende de fetch) ou `/msx/preview`
(que já monta a página com o iframe do MSX).

No preview, os cards do Início usam `content:json:{BASE64}` (JSON inline na ação), então
dá para navegar entre as telas sem servidor. Já as URLs de stream (`http://<provedor>/…`)
são HTTP: no navegador em HTTPS o mixed content bloqueia a reprodução — **na TV funciona**,
porque o app MSX roda em contexto local.

## Configuração (`appsettings.json`)

```json
"Xtream": {
  "BaseUrl": "http://<provedor>",
  "PublicBaseUrl": "",
  "DefaultVodCategory": "25",
  "DefaultSeriesCategory": "3178",
  "PosterItemsPerPage": 12,
  "MaxPosterItems": 672,
  "MaxChannelItems": 1200
}
```

- `BaseUrl` — servidor Xtream Codes.
- `PublicBaseUrl` — URL pública do app (ex.: `https://ehiptv.seu-dominio.com`). Deixe
  vazio em dev (usa scheme+host da requisição). Em produção, atrás de proxy, o `Host`
  chega como `localhost:3000`; o app usa o `PublicBaseUrl` e **reaplica o prefixo de
  credencial** do host original na frente do host público.
- `MaxPosterItems` — teto de itens por grade (o MSX aceita no máximo 56 páginas em
  `pages`, 56 × 12 = 672 itens).

## Credenciais (fora do repositório)

O repositório é público: **nenhuma credencial é versionada**. O app lê um arquivo local de
secrets (ou variáveis de ambiente):

- `EHIPTV_MSX_SECRETS_FILE` → caminho do arquivo
- padrão no Windows: `%USERPROFILE%\.minimax\secrets\ehiptv-msx.json`

```json
{
  "xtream": {
    "baseUrl": "http://<provedor>",
    "username": "<usuario>",
    "password": "<senha>"
  }
}
```

Alternativa por variável de ambiente (ASP.NET Core):
`Xtream__Username`, `Xtream__Password`, `Xtream__BaseUrl`.

## Rodar local

```bash
dotnet run --urls "http://127.0.0.1:8080"
# com o prefixo de host (o que o MSX vai usar):
set Xtream__PublicBaseUrl=http://<usuario>-<senha>.localhost:8080
```

Docker:

```bash
docker build -t ehiptv-msx .
docker run -p 8080:8080 -e Xtream__PublicBaseUrl=http://<usuario>-<senha>.localhost:8080 ehiptv-msx
```

## Notas técnicas

- O provedor devolve `parent_id` como número em uns endpoints e como string em outros:
  `Models/FlexibleStringConverter.cs` aceita os dois.
- URLs de playback (mesma base do provedor):
  - live: `{base}/live/{u}/{p}/{stream_id}.m3u8`
  - filme: `{base}/movie/{u}/{p}/{stream_id}.mp4`
  - série: `{base}/series/{u}/{p}/{episode_id}.mp4` (o `episode_id` vem de
    `get_series_info`, **não** `series_id/season/episode`)
- As capas vêm de `http://lgfp.one` (Cloudflare). O app força `https` nessas URLs para
  o preview no navegador não sofrer mixed content.
- Catálogo grande (44 mil filmes, 10 mil séries, 2.736 canais): as respostas do
  `player_api.php` ficam em cache (categorias 30 min, listas 5 min).
- No MSX, `items` na raiz do Content Root **exige** `template` — sem isso o MSX ignora os
  itens em silêncio e mostra "Content Not Available".
