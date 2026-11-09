# REA-Radio-DJ

Vrije radio-playout met een ingebouwde AI-presentator. Een losse playout-engine (service) en een desktop-studio
met vrij verplaatsbare, dokbare en zwevende vensters. Muziek 24/7 automatisch of live-assist, met klokken,
vaste tijden (nieuws op :00), cartwalls, standby- en PFL-spelers, microfoon-talkover en een as-run-log.

> Status: in ontwikkeling (ronde 1). Nog niet bedoeld voor een echte zender.

## Wat het kan

- **Engine als service** – `REA-Radio-DJ.exe` speelt de uitzending, ook als de studio dicht is. Alles is te bedienen
  via REST (`/api/...`), met live status via WebSocket (`/api/ws`) of server-sent events (`/api/events`).
- **Playout** – BASS-mixer (32-bit float), crossfades op het mixpunt, loudness-normalisatie naar −16 LUFS
  (EBU R128). Automatisch of live-assist (het huidige item speelt uit, jij start het volgende).
- **Bibliotheek** – SQLite (`data/station.db`). Mappen worden categorieën. Per bestand het volledige cuemodel:
  cue-in, fade-in klaar, intro 1 en 2, outro, fade-out, mix, cue-out en een hook. Handmatig gezette cues blijven
  bij een nieuwe scan staan.
- **Cue-editor** – golfvorm met sleepbare markers, zoomen, beluisteren vanaf elk punt.
- **Planning** – uurklokken (categorie, vast item of AI-break; willekeurig, op volgorde of alleen de hook), een
  weekgrid met per uur een klok, en vaste tijden: *hard* (onderbreekt op de seconde), *zacht* (na het huidige
  nummer) of *overlay* (eroverheen, de muziek duikt). Materiaal wordt drie minuten vooruit klaargezet; een gemist
  hard event speelt nooit te laat.
- **Studio-spelers** – twee standby-spelers met stapel, een PFL-speler op een eigen uitgang, twee cartwalls van
  4 × 16 knoppen (loop, overlay, kleur) en overlays.
- **Mixer en routing** – kanalen voor automatisering, standby, cartwall, overlay, microfoon en PFL; uitzending en
  voorbeluistering op aparte geluidskaarten. Microfoon open = muziek duikt (talkover).
- **AI-presentator** – tekst via elk OpenAI-compatibel endpoint of ingebouwde sjablonen, stem via Higgs TTS 3 of
  Windows-spraak. Breaks worden vooraf gemaakt; de uitzending wacht nooit op de AI.
- **As-run** – alles wat echt is uitgezonden (log, carts, overlays, standby), per dag te bekijken en als CSV.
- **Icecast-stream** – MP3 naar Icecast of SHOUTcast v2, inclusief titel-updates.

## Snel starten (Windows 10/11, x64)

1. Installeer de [.NET 9 SDK](https://dotnet.microsoft.com/download).
2. Haal BASS op (komt niet mee in de repository):
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\fetch-bass.ps1
   ```
3. Start de engine met gegenereerde demo-audio (inclusief een nieuws-event elk uur om :00):
   ```powershell
   dotnet run --project src\ReaRadioDj.Server -- --demo
   ```
4. Start de studio:
   ```powershell
   dotnet run --project src\ReaRadioDj.Studio
   ```
   De studio verbindt met `http://127.0.0.1:8600`. Draait de engine niet, dan kun je hem vanuit het menu
   *Studio* starten. Een eenvoudige web-interface staat op <http://localhost:8600>.

Eigen muziek: zet bestanden (mp3, wav, flac, ogg, aiff) in submappen van `media\music` (elke map wordt een
categorie) en jingles in `media\jingles`, naast de plek waar je de engine start, of stel andere mappen in via
`appsettings.json`.

### De studio

- Elk venster (log, spelers, cartwalls, bibliotheek, cue-editor, mixer, planner, as-run …) kun je verslepen,
  naast elkaar of als tabblad docken, of los laten zweven (menu *Zwevend*), ook op een tweede scherm.
- Menu *Indeling*: indelingen opslaan, laden en terugzetten. De laatste indeling wordt bij afsluiten bewaard.
- Slepen vanuit de bibliotheek naar log, speler of cartknop; dubbelklik = voorbeluisteren (PFL).
- Sneltoetsen: F5 volgende, F6 automatisch/assist, F8 microfoon, F9 stop.
- Opties: `--engine <url>`, `--no-engine`, `--profile <map>` (eigen instellingenmap), `--show <venster>`,
  `--float <venster>`.

## Instellingen

Alles staat in [`src/ReaRadioDj.Server/appsettings.json`](src/ReaRadioDj.Server/appsettings.json), sectie `ReaRadioDj`.
Je kunt alles overschrijven met omgevingsvariabelen, bijvoorbeeld `ReaRadioDj__OutputDevice=0`.

| Instelling | Betekenis |
|---|---|
| `OutputDevice` | `-1` standaard geluidskaart, `0` geen geluidskaart (alleen streamen), `1..` een specifiek apparaat (zie `/api/devices`) |
| `Mix.TargetLufs` | doel-loudness, standaard −16 |
| `Rotation.ArtistSeparation` / `SongSeparation` | niet dezelfde artiest / hetzelfde nummer binnen zoveel nummers |
| `Routing.PreviewDevice` | geluidskaart voor PFL (voorbeluisteren) |
| `Routing.MicEnabled` / `MicDevice` / `MicDuckLevel` | microfoon opnemen, invoerapparaat, muziekniveau bij open microfoon |
| `Routing.OverlayDuckLevel` / `StandbyPlayers` | muziekniveau onder overlays, aantal standby-spelers |
| `Presenter.Llm.BaseUrl` / `Model` | bijvoorbeeld `http://localhost:11434` en `llama3.1` voor Ollama; leeg = sjablonen |
| `Presenter.Tts.Provider` | `sapi` (Windows) of `higgs` |
| `Presenter.Tts.BaseUrl` | Higgs-server, standaard `http://localhost:8001` |
| `Presenter.Tts.Tags` | Higgs-stuurtags vóór elke break, bijv. `<\|emotion:happy\|>` |
| `Stream.*` | Icecast-server (`host:port/mount`), wachtwoord, bitrate |

### Higgs TTS 3 gebruiken

Higgs draait als aparte server, het liefst op Linux of in WSL met een NVIDIA-GPU:

```bash
hf download bosonai/higgs-tts-3-4b
sgl-omni serve --model-path bosonai/higgs-tts-3-4b --port 8001
```

Zet daarna `Presenter.Tts.Provider` op `higgs`. Voor een eigen stem vul je `ReferenceAudio` (een pad of URL die de
**server** kan lezen) en `ReferenceText` (wat er in dat fragment gezegd wordt) in. Faalt Higgs, dan valt
REA-Radio-DJ terug op Windows-spraak.

## Opbouw

```
src/ReaRadioDj.Core       audio-engine en mixer (BASS), analyse, bibliotheek (SQLite), klokken, vaste tijden,
                          studio-spelers, cartwall, as-run, AI-presentator, Icecast
src/ReaRadioDj.Contracts  de berichten (DTO's) tussen engine en studio
src/ReaRadioDj.Server     de engine-service: REST, WebSocket, server-sent events, automatisering, web-UI
src/ReaRadioDj.Studio     de desktop-studio (Avalonia + Dock): dokbare en zwevende vensters
tests/ReaRadioDj.Tests    unit tests en audiotests (draaien op het "no sound"-apparaat van BASS)
scripts/fetch-bass.ps1   downloadt BASS naar /native
```

De mixer bestaat uit geneste BASS-mixers: uitgangen (uitzending, PFL) → kanalen → bronnen. Een controlelus van
10 ms start het volgende log-item op het mixpunt. Ducken (microfoon, overlays, AI-breaks) gaat per kanaal met een
reden, zodat meerdere oorzaken elkaar niet in de weg zitten. De engine speelt alleen materiaal dat klaarstaat.

## Tests

```powershell
dotnet test
```

De unit tests dekken cuepunten, klokplanning, vaste tijden, de database (categorieën, klokken, weekgrid, events,
as-run) en het log. De audiotests draaien alleen als BASS aanwezig is: ze spelen echte audio af op het stille
apparaat (engine, cartwall, standby, ducken, bibliotheekscan) en starten een nep-Icecast-server voor de stream.

## Licentie

GPL-3.0 of later, met een aanvullende toestemming om met BASS te linken. Zie [NOTICE.md](NOTICE.md) voor BASS,
Higgs TTS 3 en de andere onderdelen.

REA-Radio-DJ is een eigen ontwerp. Het werkt functioneel vergelijkbaar met bekende playout-programma's, maar
gebruikt geen code, schermen, namen of bestandsformaten daarvan.

## Bediening op afstand

Alles wat je in de studio kunt, kan ook met tekstcommando's (`NEXT`, `AUTO OFF`, `CART 1 5`, `MIC TOGGLE`,
`VOLUME log 80`, `STREAM START` …). De lijst staat in de studio onder **Studio › Bediening en sneltoetsen** en op
`GET /api/commands`. Dezelfde commando's werken via:

- **HTTP**: `POST /api/command` met `{"text":"NEXT"}`
- **TCP** (poort 8601): één commando per regel, antwoord `OK …` of `ERR …`; met wachtwoord eerst `AUTH <wachtwoord>`
- **OSC** (UDP 8602): `/rea/next`, `/rea/cart/1/5`, `/rea/mic/toggle`, `/rea/cmd "AUTO OFF"` (waarde 0 = loslaten, genegeerd)
- **Sneltoetsen** in de studio (vrij in te stellen) en **vaste tijden** in de planner (actie *Commando uitvoeren*)

Standaard luistert dit alleen op deze computer. Voor het netwerk in `appsettings.json`:

```json
"ReaRadioDj": { "Control": { "BindAddress": "0.0.0.0", "TcpPort": 8601, "OscPort": 8602, "Password": "geheim" } }
```

De planner kent verder **regels** (bijv. maximaal 2 uit dezelfde categorie op rij; overslaan of uitstellen) en
**externe streams**: een Icecast/SHOUTcast-URL die in een tijdvenster wordt bewaakt en, zodra hij live is, het
programma overneemt of eroverheen speelt.

