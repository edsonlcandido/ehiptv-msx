# Eh! IPTV no Media Station X

App MSX (Media Station X) que serve o catálogo do **Xtream Codes** como páginas JSON
do MSX. Roda como uma API ASP.NET: o app MSX busca o `start.json`, o `menu.json` e
as telas de conteúdo — tudo gerado a partir do provedor, em tempo real.

## As 4 telas

| # | Tela | Conteúdo |
|---|------|----------|
| 1 | **Início** | 3 cards: Canais ao vivo / Filmes e VOD / Séries e episódios |
| 2 | **Canais** | Lista de categorias de TV (linhas largas) → canais da categoria (logo + nome), que abrem o player HLS |
| 3 | **Filmes** | Barra lateral de categorias à esquerda + grade de pôsteres verticais à direita; rolar para baixo carrega mais filmes → reproduz (`/movie/….mp4`) |
| 4 | **Séries** | Mesmo desenho da tela de Filmes → página de episódios (`S01E01 - …`) → reproduz (`/series/….mp4`) |

O menu lateral (coluna da esquerda) traz `Início`, `Canais`, `Filmes`, `Séries` e todas as
categorias de cada seção.

## Filmes e Séries: navegador no estilo XCIPTV

As telas 3 e 4 usam a **grade comprimida do MSX** (`compress: true` → 16×8 em vez de 12×6)
e um Content Root do tipo `list`, montado com `pages` posicionadas à mão:

```
┌──────────────┬───────────────────────────────────┐
│ 0,0  3 cols  │  cartaz 6,0  8,0  10,0  12,0 14,0│  ← 5 cartazes (2×4) + 1 linha de título
│ 0,1  3 cols  │                                   │
│ 0,2  ...     │  cartaz 6,4  8,4  10,4  12,0 14,0│  ← 5 cartazes
│ 3,0  3 cols  │                                   │
│ 3,1  ...     │                                   │
└──────────────┴───────────────────────────────────┘
   categorias          pôsteres (proporção vertical)
```

- **Barra lateral (colunas 0–5)**: duas sub-colunas com as categorias; a categoria ativa
  fica com outra cor (`msx-blue`) e a última vaga é **“Mais categorias”**, que abre a
  lista completa (`/msx/content/vod-categories.json`).
- **Grade (colunas 6–15)**: 5 cartazes por linha × 2 linhas = **10 por página**. Cada
  cartaz ocupa 2 colunas × 4 linhas; com o item do tipo `separate`, 3 linhas são de imagem
  (240×405 px em 1080p — proporção de poster vertical) e 1 linha é a faixa do título.
- **Scroll vertical**: o tipo `list` empilha as páginas na vertical. Cada página seguinte
  traz mais 10 cartazes da mesma categoria, então navegar para baixo carrega conteúdo novo.
- A tela abre focando o primeiro cartaz (e não a barra lateral).

O limite de itens por tela é `MaxPosterItems` (padrão 672 → 68 páginas de 10 cartazes).

## A credencial vai no primeiro trecho da URL

O usuário e a senha **sempre ficam no primeiro trecho do host** (prefixo) e valem para
todas as chamadas que o MSX faz:

```
http://<usuario>-<senha>.localhost:8080/msx/start.json
http://<usuario>-<senha>.seu-dominio.com/msx/start.json
```

Não existe mais sub-caminho com usuário/senha: todas as URLs geradas são
`{host}/msx/menu.json` e `{host}/msx/content/…`.

O MSX sempre busca `{SERVER}/msx/start.json`; ao responder, o app devolve um Start Object
cujo `parameter` aponta para a **tela inicial** (com os 3 itens), e todas as URLs geradas
(menu, telas, links de conteúdo) mantêm o mesmo host. Nenhum usuário/senha é digitado na
TV: basta configurar o domínio no app MSX.

Ordem de resolução da credencial (`Services/CredentialResolver.cs`):

1. **host** — `<usuario>-<senha>` no primeiro trecho (host ou `X-Forwarded-Host`)
2. **query** — `?u=usuario&p=senha` (só para teste)
3. **secret local** — arquivo de secrets (fallback de dev/servidor)

Atrás de um proxy o `Host` chega interno (`localhost:3000`); o app usa o `X-Forwarded-Host`
e o `X-Forwarded-Proto` para montar as URLs públicas.

## Endpoints

| Rota | Descrição |
|------|-----------|
| `GET /msx/start.json` | Start Object (aponta para a tela inicial) |
| `GET /msx/menu.json` | Menu Root (barra lateral, opcional) |
| `GET /msx/content/home.json` | Tela 1 — Início (os 3 itens) |
| `GET /msx/content/live.json` | Tela 2 — Canais (categorias) |
| `GET /msx/content/live/{categoryId}.json` | Canais da categoria |
| `GET /msx/content/vod.json` | Tela 3 — Filmes (navegador com barra lateral) |
| `GET /msx/content/vod/{categoryId}.json` | Filmes da categoria (mesmo navegador) |
| `GET /msx/content/vod-categories.json` | Lista completa de categorias de filmes |
| `GET /msx/content/series.json` | Tela 4 — Séries (navegador com barra lateral) |
| `GET /msx/content/series/{categoryId}.json` | Séries da categoria (mesmo navegador) |
| `GET /msx/content/series-categories.json` | Lista completa de categorias de séries |
| `GET /msx/content/series-info/{seriesId}.json` | Episódios da série |
| `GET /player_api.php` | API Xtream Codes padrão (veja abaixo) |
| `GET /msx/diagnostico.json` | Diagnóstico do provedor (ver abaixo) |
| `GET /msx/preview` | Preview no `msx.benzac.de` com o JSON em base64 |
| `GET /msx/preview.json` | Menu compacto (JSON autocontido) |
| `GET /health` | Health check |

Todas as respostas JSON devolvem `Access-Control-Allow-Origin` echoing a origem +
`Access-Control-Allow-Credentials` (o MSX usa `withCredentials=true`).

### Página inicial sem barra lateral

O `start.json` responde com `parameter: "content:{…}/msx/content/home.json"` — e não
`menu:…`. É essa diferença que faz o MSX **não mostrar a barra lateral esquerda**: a
tela inicial fica em tela cheia com os 3 cards (TV / Filmes / Séries). O menu completo
(com todas as categorias) continua disponível em `/msx/menu.json`.

## Provedor bloqueado (404): cadeia de candidatos e relay

Alguns provedores bloqueiam o IP do servidor (a resposta é **404** do nginx/Cloudflare,
não 401). Para isso o app:

1. Tenta as bases em ordem: `BaseUrl`, depois `FallbackUrls`, e por último o
   `ApiProxyUrl` se estiver configurado. A base que respondeu é guardada em memória e
   tentada primeiro nas próximas chamadas.
2. Deixa o diagnóstico pronto em `GET /msx/diagnostico.json`: mostra o que o app enxerga
   do request (Host, `X-Forwarded-Host`, `X-Forwarded-Proto`), o DNS de cada host, um
   teste de saída para a internet (`example.com`, `google.com`) e a resposta de cada base
   candidata. Se nem `example.com` responder, o problema é a saída do servidor; se
   responder e só o provedor falhar, é bloqueio do provedor.

Se **nenhum** domínio do provedor for alcançável do servidor, configure um relay
(qualquer proxy que forwards `GET /player_api.php?username=…&password=…&action=…` para o
provedor e devolva o JSON):

```
Xtream__ApiProxyUrl=https://seu-relay.exemplo.com
```


## O app também é um servidor Xtream Codes padrão

Além das telas do MSX, a aplicação expõe a **API do Xtream Codes** (`player_api.php`)
no padrão de IPTV, com o mesmo usuário e senha:

```
GET /player_api.php?username=<usuario>&password=<senha>
GET /player_api.php?username=<usuario>&password=<senha>&action=get_live_categories
GET /player_api.php?username=<usuario>&password=<senha>&action=get_live_streams&category_id=1
GET /player_api.php?username=<usuario>&password=<senha>&action=get_vod_categories
GET /player_api.php?username=<usuario>&password=<senha>&action=get_vod_streams&category_id=25
GET /player_api.php?username=<usuario>&password=<senha>&action=get_vod_info&vod_id=443784
GET /player_api.php?username=<usuario>&password=<senha>&action=get_series_categories
GET /player_api.php?username=<usuario>&password=<senha>&action=get_series&category_id=3178
GET /player_api.php?username=<usuario>&password=<senha>&action=get_series_info&series_id=10640
```

A resposta é a do provedor, com o `direct_source` de cada item preenchido no padrão
Xtream Codes (`{base}/live|movie|series/{usuario}/{senha}/{id}.{ext}`). Isso permite
apontar qualquer player de IPTV (TiviMate, IPTV Smarters, Perfect Player) para o
próprio domínio, com o mesmo usuário e senha.

Qualquer parâmetro que não seja credencial ou `action` é repassado ao provedor
(`category_id`, `series_id`, `vod_id`, `stream_id`, `limit`…).

## Instalar no app MSX (TV / desktop)

1. Publique o app (Docker/Easypanel) e aponte o domínio para ele.
2. Crie um registro DNS curinga: `*.seu-dominio.com` → mesmo IP/serviço
   (ex.: `<usuario>-<senha>.seu-dominio.com`).
3. No app MSX: **Settings → Start Parameter → Setup** → informe
   `<usuario>-<senha>.seu-dominio.com` (sem `http://`), o lock de segurança e confirme.
4. O app carrega o catálogo e o usuário navega pelas telas.

> Com wildcard DNS, o prefixo de credencial funciona em qualquer rede.
> Sem wildcard, use o domínio fixo e passe a credencial via
> `?u=…&p=…` (útil para links diretos, não para o Setup do app).

## HTTP, e não HTTPS (de propósito)

O provedor **só serve HTTP** (o `server_info` vem com `https_port: null` e o handshake
TLS é recusado). Se o app MSX fosse servido em HTTPS, o navegador bloquearia as
streams HTTP por *mixed content* — o player ficaria preto.

Por isso o app gera **todas** as URLs em HTTP (`Xtream__PublicScheme: "http"`). Não
aponte o domínio para um certificado HTTPS: em produção use `http://<usuario>-<senha>.dominio`.

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
dá para navegar entre as telas sem servidor. Já as URLs de stream (`http://<servidor>/…`)
são HTTP: no navegador em HTTPS o mixed content bloqueia a reprodução — **na TV funciona**,
porque o app MSX roda em contexto local.

## Configuração (`appsettings.json`)

**O repositório é público: nenhum endereço de provedor é versionado.** As bases do
provedor entram por variável de ambiente (ver abaixo) ou pelo arquivo de secrets.

```json
"Xtream": {
  "BaseUrl": "",
  "FallbackUrls": [],
  "ApiProxyUrl": "",
  "PublicBaseUrl": "",
  "PublicScheme": "http",
  "UseServerInfo": true,
  "PlaybackBaseUrl": "",
  "DefaultVodCategory": "25",
  "DefaultSeriesCategory": "3178",
  "MaxPosterItems": 672,
  "MaxChannelItems": 1200
}
```

- `BaseUrl` — servidor Xtream Codes principal. **Vem do ambiente** (`BASE_URL`).
- `FallbackUrls` — outros domínios do mesmo provedor, tentados quando o principal
  responde 404 (o provedor bloqueia o IP do servidor). **Vem do ambiente**
  (`FALLBACK_URL`, separados por vírgula).
- `ApiProxyUrl` — relay opcional (ver acima). **Vem do ambiente** (`PROXY_URL`).
- `PublicBaseUrl` — URL pública do app (ex.: `http://ehiptv.seu-dominio.com`). Deixe
  vazio em dev (usa scheme+host da requisição). Em produção, atrás de proxy, o `Host`
  chega como `localhost:3000`; o app usa o `X-Forwarded-Host` e **reaplica o prefixo de
  credencial** na frente do host público.
- `PublicScheme` — esquema das URLs geradas. **Padrão `http`** (o provedor só serve
  HTTP; servir o app em HTTPS daria mixed content e o player não reproduz). Vazio = usa
  o esquema da requisição.
- `UseServerInfo` — deriva a base das URLs de playback do `server_info` devolvido pelo
  `player_api.php` (padrão IPTV Xtream Codes). Desligue (`false`) para usar sempre o
  host do painel (`BaseUrl`).
- `PlaybackBaseUrl` — base fixa das URLs de playback; tem prioridade sobre o
  `server_info`. Vazio = usar o `server_info`.
- `MaxPosterItems` — teto de cartazes por tela de Filmes/Séries (padrão 672 → 68 páginas
  de 10 cartazes; o MSX aceita no máximo 1024 páginas no tipo `list` e 256 itens por página).

## Endereços do provedor (fora do repositório)

### Variáveis de ambiente (Docker / Easypanel)

| Variável | O que é |
|----------|---------|
| `BASE_URL` | base do Xtream Codes (ex.: `http://provedor.exemplo.com`) |
| `FALLBACK_URL` | bases alternativas, separadas por vírgula |
| `PROXY_URL` | relay opcional do `player_api.php` |
| `Xtream__Username` / `Xtream__Password` | credenciais (quando não vierem do arquivo de secrets) |

Guarde um arquivo `.env` **fora do repositório** (ex.:
`%USERPROFILE%\.minimax\secrets\ehiptv-msx.env`) e use no Docker:

```bash
docker run -p 8080:8080 \
  --env-file /caminho/ehiptv-msx.env \
  ehiptv-msx
```

```bash
# ehiptv-msx.env (fora do repo)
BASE_URL=http://provedor.exemplo.com
FALLBACK_URL=http://outro.provedor.exemplo.com
```

### Arquivo de secrets (dev / servidor próprio)

O app lê um arquivo local de secrets (ou variáveis de ambiente):

- `EHIPTV_MSX_SECRETS_FILE` → caminho do arquivo
- padrão no Windows: `%USERPROFILE%\.minimax\secrets\ehiptv-msx.json`

```json
{
  "xtream": {
    "baseUrl": "http://provedor.exemplo.com",
    "fallbackUrls": [ "http://outro.provedor.exemplo.com" ],
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
- URLs de playback no padrão IPTV Xtream Codes, com a base vinda do `server_info` do
  `player_api.php`:
  - live: `{base}/live/{u}/{p}/{stream_id}.m3u8`
  - filme: `{base}/movie/{u}/{p}/{stream_id}.mp4`
  - série: `{base}/series/{u}/{p}/{episode_id}.mp4` (o `episode_id` vem de
    `get_series_info`, **não** `series_id/season/episode`)
  - No `player_api.php` do próprio app, o `direct_source` usa a mesma base com `.ts`
    para live e a extensão do contentor nos demais.
- As capas vêm de `http://lgfp.one` (Cloudflare). O app força `https` nessas URLs para
  o preview no navegador não sofrer mixed content.
- Catálogo grande (44 mil filmes, 10 mil séries, 2.736 canais): as respostas do
  `player_api.php` ficam em cache (categorias 30 min, listas 5 min, `server_info` 30 min).
  O cache guarda o JSON bruto e cada requisição desserializa uma cópia, para nenhum
  chamador alterar o que ficou guardado.
- No MSX, `items` na raiz do Content Root **exige** `template` — sem isso o MSX ignora os
  itens em silêncio e mostra "Content Not Available".
- As telas de Filmes e Séries usam `compress: true`, que troca a grade 12×6 pela 16×8
  (25% menor por célula). É o que permite a barra lateral de categorias ao lado da grade
  de cartazes com proporção vertical. A barra lateral e a grade são `pages` posicionadas à
  mão (`layout: "x,y,w,h"`), não `template` + `items`.
- O prefixo das categorias ("Filmes | ", "Series | ") é removido na barra lateral; o
  `headline` da tela mantém o nome completo.
