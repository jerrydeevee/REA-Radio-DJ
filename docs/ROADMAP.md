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
| ✅ Higgs op de GPU | Installatie in WSL (`tools/higgs/install.sh`, `start.sh`), model `bosonai/higgs-tts-3-4b`, CUDA-toolchain vastgezet; render getest |
| ✅ Studio-layout | Spelers met golfvorm en grote teller, cartwall-tegels die leeglopen en knipperen, planner met Dag- en Weekoverzicht, Live-decks A/B/C met eigen kleurenpalet en deckkleuren in de log |
| ✅ Wensenlijst (Spotify) | Openbare Spotify-playlist inlezen (alleen gegevens), koppelen aan de bibliotheek op ISRC of artiest + titel, in de log zetten, ontbrekende nummers exporteren; nooit audio uit Spotify |
| ✅ Muziekrapport | Rechtengegevens per nummer (ISRC, componist, label, jaar) uit tags en wensenlijst; rapport per periode voor Buma/Stemra en Sena (elke uitzending + overzicht per nummer) |

## AI-presentator en stem — vervolg

| Status | Onderdeel |
|---|---|
| ✅ | Taalmodel koppelen: elke OpenAI-compatibele server (Ollama, LM Studio, online dienst), instelbaar in de studio met proefscript; getest met Ollama `gemma3:12b` (goed Nederlands) |
| ✅ | Nabewerking: stilte weg, hoogdoorlaat 80 Hz, compressor, piek -1 dBFS; loudness op stationsnorm en samplerate doet de playout zelf |
| ✅ | Muziekbed onder de break (aanloop, dip onder de stem, uitfade met het laatste woord); talk-up over de intro tot de zang. Outro-jingle na de break |
| ✅ | Persona's per dagdeel: naam, karakter, stem, lengte en muziekbed per dagen + uren |
| ✅ | Script goedkeuren, bewerken of opnieuw laten schrijven vóór het inspreken (niet op tijd goedgekeurd = jingle) |
| ✅ | Controle met Whisper: elke break wordt teruggeluisterd en vergeleken met het script (score per woord); te laag = opnieuw inspreken, daarna overslaan of uitzenden met waarschuwing. Server in `tools/whisper` (model small, GPU of CPU, geen ffmpeg nodig); getest: echte break score 0,86 |
| ✅ | Variabelen: weer (Open-Meteo), nieuwskoppen (RSS/Atom, elke kop één keer) en berichten om voor te lezen; hoogstens één per break |
| ➖ | Referentiecodes per stem vooraf berekenen: niet nodig. De Higgs-server bewaart de codes zelf na de eerste break (kloon 4,9 s eerste keer, daarna 3,1 s; standaardstem 2,3 s). Een geüploade stem bewaart alleen het geluid, niet de codes, dus bespaart niets. Winst zou alleen 1,5 s op de eerste break na een herstart zijn, en breaks worden ruim vooraf gemaakt |
| ✅ | Andere stemmen als reserve: ElevenLabs en Azure als stem-engine; elke stem kan een reservestem hebben (keten tot vier stappen, Windows-spraak als laatste redmiddel) |

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
| 🟡 | Rapportage auteursrechten: CSV staat; kolomindeling nog afstemmen op het sjabloon van de NLPO / Buma/Stemra / Sena |

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
