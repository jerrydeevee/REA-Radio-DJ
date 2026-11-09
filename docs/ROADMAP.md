# Rondeplan

Alles wordt eigen ontwerp: functies zoals een professioneel playoutsysteem ze nodig heeft, zonder teksten, namen,
schermen of bestandsformaten van andere pakketten over te nemen.

Legenda: ✅ klaar · 🟡 deels · ⬜ open

## Klaar

| Onderdeel | Wat |
|---|---|
| ✅ Ronde 1 | Engine-service, desktop-studio (dockbaar, 1-3 schermen), bibliotheek met automatische cues en LUFS, klokken, weekgrid, vaste tijden, standby/PFL-spelers, 2 cartwalls, mixer met ducking, as-run, Icecast, REST/WebSocket, tray-icoon |
| ✅ Ronde 2a | Commandobus (HTTP, TCP, OSC), sneltoetsen, planner-commando's/herinneringen/alarmen, scriptregels, externe streams bewaken |
| ✅ Studio-UX | Aftelbalk (oranje 20 s, rood knipperend 10 s), intro-teller, aanraakknoppen, weergaven Automatisering/Live-assist |
| ✅ Studioklok | Studio-, analoge en digitale klok, schaalbaar; grote tellers met flitskleur aan het einde |
| ✅ Stem (Higgs TTS 3) | Stemprofielen (Higgs of Windows), stuurtags, variatie, klonen alleen met vastgelegde toestemming, proefopname op PFL, GPU- en serverstatus, server starten/stoppen vanuit de studio |

## AI-presentator en stem — vervolg

| Status | Onderdeel |
|---|---|
| ⬜ | Higgs-server installeren op de GPU (WSL + SGLang-Omni of vLLM-Omni, model ~4B downloaden) en koppelen via `ServerCommand` |
| ⬜ | Taalmodel koppelen (Ollama/LM Studio lokaal of een API) voor de scripts; nu alleen sjabloonteksten |
| ⬜ | Nabewerking: 24 → 48 kHz, hoogdoorlaat, compressie, loudness op stationsnorm |
| ⬜ | Music bed onder de break, outro-element, talk-up tot de zang |
| ⬜ | Persona's en dayparts (stem + stijl per tijdvak in een rooster) |
| ⬜ | Script goedkeuren of bewerken vóór het renderen |
| ⬜ | Controle met Whisper: klopt de gesproken tekst met het script |
| ⬜ | Variabelen: weer, RSS/nieuws, luisteraarsberichten |
| ⬜ | Referentiecodes per stem vooraf berekenen (sneller klonen) |
| ⬜ | Andere stemmen als reserve (ElevenLabs, Azure) |

## Ronde 2b — Presentatie

| Status | Onderdeel |
|---|---|
| ⬜ | Voicetracking: opnemen tussen twee logitems in drie stappen (start, volgende, stop) |
| ⬜ | Overgangseditor met golfvormen, inserts en volume-envelopes |
| ⬜ | Nieuwsbeheer: blokken, RSS-bronnen, opener/sluiter als vaste tijd |

## Ronde 3 — Koppelingen en reclame

| Status | Onderdeel |
|---|---|
| ⬜ | Externe muziekplanning: logs importeren met import-profielen, bibliotheek exporteren |
| ⬜ | Database-bibliotheken (ODBC) en metadata-opzoeking |
| ⬜ | Reclamebeheer: adverteerders, campagnes, blokken, bewijs van uitzending |
| 🟡 | Hardware-bediening: commandobus staat; MIDI, serieel, GPIO-kaarten, Ember+, Livewire+ en mengtafels nog koppelen |
| ⬜ | Rapportage auteursrechten (Buma/Stemra, Sena) |

## Ronde 4 — Meerdere studio's en op afstand

| Status | Onderdeel |
|---|---|
| ⬜ | Gebruikers, rollen en rechten |
| ⬜ | Multi-studio: master/slave met hartslag en automatische overname |
| ⬜ | Centrale bibliotheekserver |
| ⬜ | Voicetracking op afstand |
| ⬜ | Remote-app: Android-tablet (bedienen) en telefoon (bewaken) |

## Audio en techniek

| Status | Onderdeel |
|---|---|
| ⬜ | ASIO-uitvoer |
| ⬜ | Meerdere streamservers en codecs (AAC, Opus), HLS |
| ⬜ | Opnemen (programma, mic) en automatische opname |
| ⬜ | DSP/VST per kanaal, ReplayGain en BPM-detectie |
| ⬜ | Watchdog en herstel na een crash |
