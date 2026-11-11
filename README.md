# Eh! IPTV no Media Station X

App MSX (Media Station X) que serve o catálogo do **Xtream Codes** como páginas JSON
do MSX. Roda como uma API ASP.NET: o app MSX busca o `start.json`, o `menu.json` e
as telas de conteúdo — tudo gerado a partir do provedor, em tempo real.

## As 4 telas

| # | Tela | Conteúdo |
|---|------|----------|
| 1 | **Início** | 3 cards: Canais ao vivo / Filmes e VOD / Séries e episódios |
| 2 | **Canais** | Coluna nativa do app com **todas as categorias de TV** + lista de canais no estilo *Channel List* do MSX (logo, nome, **programa no ar**, horário e barra de progresso) |
| 3 | **Filmes** | Coluna nativa do app com **todas as categorias de filmes** + grade de pôsteres verticais que **carrega mais ao rolar** (Paging Plugin) → reproduz (`/movie/….mp4`) |
| 4 | **Séries** | Mesmo desenho da tela de Filmes → **página de detalhe** (vitrine com capa + sinopse, episódios separados por temporada) → reproduz (`/series/….mp4`) |

O menu lateral (coluna da esquerda) traz `Início`, `Canais`, `Filmes`, `Séries` e todas as
categorias de cada seção. A tela de Canais abre o menu de TV (`/msx/menu/live.json`), que
lista **todas** as categorias de canal do provedor.

## Tela de Canais no estilo "Channel List" (com EPG)

A tela 2 **não usa a barra lateral dentro do conteúdo**: as categorias de TV ficam na
**coluna nativa do app** (o menu do MSX, que rola e mostra todas — 45 no provedor atual),
e o conteúdo mostra a lista de canais da categoria escolhida. O card "Canais ao vivo" do
Início abre esse menu (`menu:{host}/msx/menu/live.json`).

A lista é uma página do Content Root do tipo `list`, montada com `pages` posicionadas à mão,
igual ao demo *Channel List* do MSX:

```
┌────────┬─────────────────────────────────────────────┐
│ logo   │ NOME DO CANAL                                │
│        │ Programa no ar                               │
│        │ 16:10 - 16:40 (30 min) | 25 min left         │
│        │ ▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │ ← progresso
└────────┴─────────────────────────────────────────────┘
```

- **4 canais por página**, cada um ocupando **1,5 linha** da grade 12×6 (`layout` + `offset`),
  para o bloco de texto (nome, programa, horário e barra) caber inteiro.
- O item é do tipo `default`, com o **logo à esquerda** (`imageWidth: 1.6` — o MSX mede a
  imagem na mesma unidade do `layout`, então 1,6 de 12 colunas) e `imageFiller: "fit"`
  (logomarca inteira, sem corte).
- O nome do canal vai em `text` (em branco, como no demo) e o **programa no ar** e o
  **horário** são injetados pelo MSX no `titleHeader` e no `titleFooter` do item.
- **EPG**: cada item carrega um `live` do tipo `setup`, que roda
  `execute:service:{host}/msx/live/epg?stream_id=…` quando o canal fica visível. O serviço
  responde `update:content:{id}` com um `live` do tipo `schedule` (programa, faixa de
  horário e contagem regressiva) e uma ação `over` que refaz a chamada quando o programa
  termina — a lista se atualiza sozinha. O `stream_id` vem do provedor (`get_short_epg`);
  título e descrição chegam em base64 e são decodificados pelo app.
- Canais sem EPG no provedor ficam sem programa (e o serviço tenta de novo em 1 hora).

## Filmes e Séries: coluna nativa + Paging Plugin

As telas 3 e 4 **não usam a barra lateral dentro do conteúdo**: as categorias ficam na
**coluna nativa do app** (o menu do MSX, que rola e mostra todas — 36 categorias de filmes
e 35 de séries no provedor atual), e o conteúdo abre em tela cheia com a grade de cartazes.
O card "Filmes e VOD" do Início abre esse menu (`menu:{host}/msx/menu/vod.json`).

O carregamento das capas é feito pelo **Paging Plugin** do MSX (o mesmo da seção
"página de plugin" da documentação do MSX):

```
┌──────────────┬─────────────────────────────────────────┐
│  Filmes      │  cartaz  cartaz  cartaz  cartaz  …      │ ← 8 cartazes (2×4)
│ Mais Assist. │                                         │
│ Lancamentos  │  cartaz  cartaz  cartaz  cartaz  …      │
│ 4K           │                                         │
│  …           │                                         │
└──────────────┴─────────────────────────────────────────┘
   categorias                pôsteres (proporção vertical)
```

- **O menu** (`/msx/menu/vod.json` e `/msx/menu/series.json`) tem um item por categoria.
  O `data` de cada item é uma ação do plugin:

  ```
  request:interaction:{host}/msx/service/vod/{id}.json?offset={OFFSET}&limit={LIMIT}|16@{plugin}
  ```

  > Atenção: no campo `data` de um item de menu **não** se usa o prefixo `content:` —
  > `content:` é sintaxe de ação e o MSX trata o valor inteiro como URL (a tela fica
  > "Data Load Error"). O formato do menu é `request:interaction:{DATA_ID}@{URL}`.

- **O content service** (`/msx/service/vod/{id}.json` e `/msx/service/series/{id}.json`)
  devolve uma página de cartazes no envelope do MSX:

  ```json
  {"response":{"status":200,"text":"OK","data":{ "type":"list", "template":{…}, "items":[…] }}}
  ```

  A primeira resposta precisa de `template` **e** `items`; as páginas seguintes só de `items`.
- **A grade** usa a grade comprimida do MSX (`compress: true` → 16×8 em vez de 12×6) e um
  Content Root do tipo `list` com `template` + `items` (sem `pages`). O template define o
  cartaz (`type: separate`, `layout: "0,0,2,4"`, `imageFiller: cover`): 2 colunas × 4 linhas
  por cartaz, com a última linha reservada para o título — proporção vertical de poster.
  São **8 cartazes por linha × 2 linhas = 16 por página**.
- **O scroll infinite** é do plugin: quando o último cartaz da página fica visível, ele pede
  a próxima página (`offset+limit`) ao content service e recarrega o conteúdo. O cabeçalho
  mostra o progresso (`Lancamentos (18/48)`) e o `extension` mostra o total da categoria
  (`101 filmes`).
- O limite de itens por categoria é `MaxPosterItems` (padrão 672). O `limit` de cada página
  é `PagingLimit` (padrão 16 — uma página cheia da grade comprimida).

## Página de detalhe da série: vitrine + temporadas + episódios

Clicar num cartaz de série abre a **vitrine**: a capa, a sinopse e os metadados da série
(Xtream Codes) ficam num **cabeçalho fixo** (`header` do Content Root) e, embaixo, a lista
de episódios **separada por temporada**.

```
┌──────────────────────────────────────────────────────────────────────┐
│ ┌────────┐  O Distrito 21 do Departamento de Polícia de Chicago é     │
│ │ capa   │  composto pelos policiais uniformizados que lidam com…   │
│ │ (3 col)│  Crime, Drama                              ┌────┐        │
│ └────────┘                            CHICAGO P D : DISTRITO 21   │
│ ┌──────────────┐ ┌──────────────┐                                  │
│ │   Assistir   │ │  Temporadas  │                                  │
│ └──────────────┘ └──────────────┘                                  │
├──────────────────────────────────────────────────────────────────────┤
│  TEMPORADA 1                                          15 EPISÓDIOS   │
│  S01E01      Ap?s uma s?rie de brutais assassinatos…      43 MIN      │
│  S01E02      Antonio acredita que um grupo colombiano…    43 MIN      │
└──────────────────────────────────────────────────────────────────────┘
```

- **A vitrine** (`header` do Content Root) tem 2 linhas de grade: capa à esquerda
  (`imageWidth: 3`, `imageFiller: fit`), sinopse (`text`), ano/gênero (`titleHeader`), nota
  (`badge`) e o título grande (`title`). O `header` exige `template` **e** `items` presentes
  no Content Root.
- **Botões**: `Assistir` (primeiro episódio da série) e `Temporadas` (a lista de temporadas).
  São itens do tipo `button` na página de cabeçalho, posicionados à mão (`layout`).
- **Quebra de temporada**: cada temporada começa com um item do tipo `teaser` (rótulo grande
  na faixa da tela). O MSX **não desenha linha divisória fina** em lista de conteúdo — o
  `offset` com altura negativa apaga o item e o `type: "space"` não escreve o rótulo — então a
  faixa do rótulo é o que faz esse papel.
- **Linha de episódio**: 1 linha da grade, com o **código à esquerda** (`S01E01`), a
  **sinopse à direita** (`separation: 0.18` + `alignment: "separation-text-right"`) e a
  **duração no canto** (`stamp`). O `truncation: "text"` corta a sinopse com reticências (sem
  ele o texto vaza e fica cortado no meio da linha).
- **Continuar de onde parou**: cada episódio leva `properties: { "resume:key": "url" }`, e o
  MSX guarda a posição do playback localmente — ao abrir o episódio de novo, ele continua
  do ponto em que parou.
- **Metadados da série**: o provedor **ignora `series_id` no `get_series`** (devolve o
  catálogo inteiro), então o cartaz leva o `category_id` da categoria de origem na URL
  (`…/series-info/{id}.json?category_id=…`) e a série é procurada dentro da lista dessa
  categoria. Sem o `category_id`, o app varre 6 categorias em paralelo até achar.
- **Temporadas**: `seasons[]` do `get_series_info` traz nome, contagem de episódios, data e
  **capa por temporada** (imagem do TMDB). A tela "Temporadas" lista todas com a capa e abre
  os episódios de uma temporada (`…/series-season/{id}/{n}.json`). Temporada sem episódios no
  provedor (o caso dos "Especiais", season 0) é omitida.
- Limites de texto: sinopse da vitrine 200 caracteres e sinopse do episódio 220 (o MSX corta
  o resto) — é o que mantém o JSON leve (uma série com 800 episódios fica em ~300 KB).

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
| `GET /msx/menu/live.json` | Menu de TV (coluna nativa do app com todas as categorias) |
| `GET /msx/content/live.json` | Tela 2 — Canais (lista no estilo *Channel List*) |
| `GET /msx/content/live/{categoryId}.json` | Canais da categoria (mesma lista) |
| `GET /msx/live/epg?stream_id=…` | Serviço de live data do MSX (programa no ar do canal) |
| `GET /msx/content/vod.json` | Tela 3 — Filmes (primeira página de cartazes) |
| `GET /msx/content/vod/{categoryId}.json` | Filmes da categoria (primeira página) |
| `GET /msx/menu/vod.json` | Menu de filmes (coluna nativa do app com todas as categorias) |
| `GET /msx/service/vod/{categoryId}.json?offset=…&limit=…` | Content service do Paging Plugin (filmes) |
| `GET /msx/content/vod-categories.json` | Lista completa de categorias de filmes |
| `GET /msx/content/series.json` | Tela 4 — Séries (primeira página de cartazes) |
| `GET /msx/content/series/{categoryId}.json` | Séries da categoria (primeira página) |
| `GET /msx/menu/series.json` | Menu de séries (coluna nativa do app com todas as categorias) |
| `GET /msx/service/series/{categoryId}.json?offset=…&limit=…` | Content service do Paging Plugin (séries) |
| `GET /msx/content/series-categories.json` | Lista completa de categorias de séries |
| `GET /msx/content/series-info/{seriesId}.json?category_id=…` | Detalhe da série (vitrine + episódios por temporada) |
| `GET /msx/content/series-seasons/{seriesId}.json?category_id=…` | Lista de temporadas da série |
| `GET /msx/content/series-season/{seriesId}/{season}.json?category_id=…` | Episódios de uma temporada |
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

No preview, o card de Canais do Início usa `content:json:{BASE64}` (JSON inline na ação),
então dá para navegar na lista de canais sem servidor. Os cards de Filmes e Séries abrem o
menu nativo (`menu:{host}/msx/menu/vod.json`), que precisa do servidor no ar. Já as URLs de
stream (`http://<servidor>/…`) são HTTP: no navegador em HTTPS o mixed content bloqueia a
reprodução — **na TV funciona**, porque o app MSX roda em contexto local.

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
  "DefaultLiveCategory": "1",
  "MaxPosterItems": 672,
  "MaxChannelItems": 1200,
  "PagingPluginUrl": "http://msx.benzac.de/interaction/paging.html",
  "PagingLimit": 16
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
- `MaxPosterItems` — teto de cartazes por categoria de Filmes/Séries (padrão 672).
- `MaxChannelItems` — teto de canais por tela de Canais (padrão 1200 → 150 páginas de 8 canais).
- `PagingPluginUrl` — página do plugin do MSX que carrega as capas sob demanda (padrão:
  a oficial, `http://msx.benzac.de/interaction/paging.html`). Se sair do ar, aponte para uma
  cópia servida pelo próprio app — o app só precisa que a página carregue o plugin oficial
  de `paging.js`.
  > **Mantenha em `http://`**: o `paging.js` reescreve `http://` por `https://` na URL do
  > content service quando a **página do plugin** é carregada por HTTPS. Como o app é
  > servido em HTTP (o provedor só serve HTTP), uma URL de plugin em HTTPS quebraria o
  > carregamento das capas.
- `CategoriesCacheMinutes` — minutos de cache da lista de categorias (padrão 5). O
  provedor **cria, renomeia e apaga** as categorias de evento ao longo do dia
  (ex.: `Canais | Jogos do dia` → `Canais | Eventos de Hoje`, com os canais
  `Jogos de Hoje` e `Lutas do Dia`), então a lista precisa ser curta.
- `DefaultLiveCategory` / `DefaultVodCategory` / `DefaultSeriesCategory` — categoria que abre
  cada tela (`"1"` = *Canais | Abertos*). Se o id não existir no provedor, a primeira
  categoria da lista é usada.

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
  `player_api.php` ficam em cache (categorias 5 min, listas 5 min, `server_info` 30 min).
  O cache guarda o JSON bruto e cada requisição desserializa uma cópia, para nenhum
  chamador alterar o que ficou guardado.
- No MSX, `items` na raiz do Content Root **exige** `template` — sem isso o MSX ignora os
  itens em silêncio e mostra "Content Not Available".
- As telas de Filmes e Séries usam `compress: true`, que troca a grade 12×6 pela 16×8
  (25% menor por célula). É o que permite 8 cartazes de proporção vertical por linha.
  A grade é `template` + `items` (sem `pages`), porque o Paging Plugin precisa dos dois.
- No campo `data` de um **item de menu**, a ação do Paging Plugin é
  `request:interaction:{DATA_ID}@{URL}` — **sem** o prefixo `content:`. Com o prefixo, o
  MSX trata o valor inteiro como URL e a tela fica em "Data Load Error".
- O MSX só atualiza um campo que **veio no próprio item**: se `titleHeader`, `titleFooter`
  ou `progress` ficarem só no `template`, o update do EPG não aparece na tela.
- O MSX chama o serviço de live data com **POST**, mantendo a query string (`?stream_id=…`)
  e mandando `{"data":null}` no corpo. O endpoint aceita `GET`/`POST` e lê o parâmetro da
  query, do form ou do JSON.
- O `imageWidth` do MSX é medido em **colunas da grade** (não é fração 0–1) e nunca pode
  ser maior que a largura do item.
- O prefixo das categorias ("Canais | ", "Filmes | ", "Series | ") é removido na barra
  lateral e no `headline` da tela.
- `truncation: "text"` corta o texto em **uma linha** com reticências (serve para a linha de
  episódio). Na vitrine da série o texto **não** é truncado: a sinopse é limitada no servidor
  para caber em 3 linhas, senão ela invade a linha do título.
- `separation` + `alignment: "separation-text-right"` divide o item em duas áreas: o título
  (código do episódio) fica à esquerda e o texto (sinopse) à direita.
- O `tag` do MSX vira uma **fita diagonal** no canto (e o `stamp`, uma etiqueta no canto
  inferior direito). Rótulos longos nesse canto não cabem.
- Item do tipo `teaser` tem título maior; `type: "space"` não escreve rótulo nenhum e
  `offset` com altura negativa **apaga** o item.
