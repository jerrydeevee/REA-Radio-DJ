# REA-Radio-DJ

Vrije radio-playout met een ingebouwde AI-presentator. Een werkend proof-of-concept: muziek 24/7 automatisch
afspelen met crossfades en loudness-normalisatie, een AI-presentator die tussen nummers praat, een web-interface
voor live-assist en een Icecast-stream.

> Status: proof-of-concept. Bedoeld om het idee te bewijzen en mee te experimenteren, nog niet voor een echte zender.

## Wat het kan

- **Playout** – BASS-mixer (32-bit float), automatische cuepunten (cue-in, mixpunt, cue-out) en crossfades.
  Elk nummer wordt genormaliseerd naar −16 LUFS (EBU R128).
- **Rotatie** – willekeurige nummers met scheiding op artiest en titel, elke N nummers een jingle en elke M nummers
  een AI-break.
- **AI-presentator** – schrijft een korte presentatie over het vorige en het volgende nummer en spreekt die in:
  - tekst via elk OpenAI-compatibel endpoint (Ollama, LM Studio, vLLM, OpenAI …), anders via ingebouwde sjablonen;
  - stem via **Higgs TTS 3** (Boson AI, via SGLang-Omni of vLLM-Omni), anders Windows-spraak (offline);
  - breaks worden ruim vóór uitzending gemaakt. Is een break niet op tijd klaar, dan speelt er een jingle:
    de uitzending wacht nooit op de AI;
  - talk-up: heeft het volgende nummer een bekende intro (`ramp`), dan start het al onder de stem en komt het
    omhoog zodra de presentator klaar is.
- **Web-UI** – nu/straks, voortgang met mixpunt, start/stop/volgende, AI-break invoegen, items verplaatsen of
  verwijderen, bibliotheek doorzoeken en toevoegen, VU-meter.
- **Icecast-stream** – MP3 naar Icecast of SHOUTcast v2, inclusief titel-updates.

## Snel starten (Windows 10/11, x64)

1. Installeer de [.NET 9 SDK](https://dotnet.microsoft.com/download).
2. Haal BASS op (komt niet mee in de repository):
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\fetch-bass.ps1
   ```
3. Start met gegenereerde demo-audio:
   ```powershell
   dotnet run --project src\ReaRadioDj.Server -- --demo
   ```
4. Open <http://localhost:8600>.

Eigen muziek: zet bestanden (mp3, wav, flac, ogg, aiff) in `media\music` en jingles in `media\jingles`, naast de
plek waar je het programma start, of stel andere mappen in via `appsettings.json`. De eerste keer worden alle
bestanden geanalyseerd; daarna komt de analyse uit `data\library-cache.json`.

Intro-lengte voor talk-up: maak naast een nummer een bestand `<nummer>.cue.json` met `{ "ramp": 12.5 }`.

## Instellingen

Alles staat in [`src/ReaRadioDj.Server/appsettings.json`](src/ReaRadioDj.Server/appsettings.json), sectie `ReaRadioDj`.
Je kunt alles overschrijven met omgevingsvariabelen, bijvoorbeeld `ReaRadioDj__OutputDevice=0`.

| Instelling | Betekenis |
|---|---|
| `OutputDevice` | `-1` standaard geluidskaart, `0` geen geluidskaart (alleen streamen), `1..` een specifiek apparaat (zie `/api/devices`) |
| `Mix.TargetLufs` | doel-loudness, standaard −16 |
| `Rotation.BreakEverySongs` / `JingleEverySongs` | hoe vaak een AI-break of jingle |
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
src/ReaRadioDj.Core      audio-engine (BASS), analyse, bibliotheek, rotatie, AI-presentator, Icecast
src/ReaRadioDj.Server    ASP.NET Core: API, server-sent events, web-UI (wwwroot) en automatisering
tests/ReaRadioDj.Tests   unit tests en audiotests (draaien op het "no sound"-apparaat van BASS)
scripts/fetch-bass.ps1   downloadt BASS naar /native
```

De engine speelt alleen materiaal dat klaarstaat. Elk log-item is een decodeerkanaal in één BASS-mixer. Een
controlelus van 10 ms start het volgende item op het mixpunt, laat het vorige uitfaden en regelt het ducken
onder AI-breaks.

## Tests

```powershell
dotnet test
```

De audiotests draaien alleen als BASS aanwezig is. Ze spelen echte audio af op het stille apparaat en starten
een nep-Icecast-server om de stream te controleren.

## Licentie

GPL-3.0 of later, met een aanvullende toestemming om met BASS te linken. Zie [NOTICE.md](NOTICE.md) voor BASS,
Higgs TTS 3 en de andere onderdelen.
