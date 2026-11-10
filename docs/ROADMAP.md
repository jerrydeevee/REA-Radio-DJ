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
| ✅ | Voicetracking: opnemen tussen twee logitems in drie stappen (start, volgende, stop) via PFL, los van de microfoonknop. Stilte eraf, timing blijft: de stem begint over de outro (muziek zachter), het volgende nummer start waar je Volgende drukte. Beluisteren, opnieuw of in de log; de nummers eromheen blijven staan bij herplannen. |
| ✅ | Log bewaard bij herstart: de engine schrijft de log (komende items en recente geschiedenis) elke seconde naar `log.json` in de datamap als er iets veranderd is, en zet hem terug bij het starten. Voicetracks, handmatige items en gemaakte AI-breaks komen terug; een break waarvan het bestand weg is wordt opnieuw gemaakt, items zonder audiobestand vallen weg. Is de log ouder dan een uur, dan worden alleen de klokitems opnieuw gepland. Getest met een harde kill: alle 9 items terug in dezelfde volgorde. Het nummer dat op dat moment liep, begint niet opnieuw |
| ✅ | Overgangseditor (venster Overgangen, of ⇄ in de programmalog): beide golfvormen op één tijdlijn, het volgende item opzij slepen om te kiezen waar het start, gele volumelijnen voor beide items (punten slepen, dubbelklik toevoegen, rechtsklik weg), een jingle of sweeper als insert in de middelste baan, snelknoppen voor uitfaden. Beluisteren mixt de overgang offline en speelt hem op PFL. Opgeslagen op het logitem en bewaard bij een herstart; geldt alleen zolang dezelfde twee items na elkaar staan. Getest in BASS: volgende nummer op 2,0 s in plaats van het mixpunt (5,5 s), insert op 1,5 s, uitgaand nummer weg na zijn fade |
| ✅ | Nieuwsbeheer (venster Nieuws): nieuwsblokken op een vaste tijd (dagen, uren, minuut, seconde; hard of zacht) met opener, het nieuws en sluiter. Het nieuws komt uit het nieuwste bestand in een map (met maximale leeftijd, bijv. een nieuwsdienst die elk uur een bulletin neerzet), uit een categorie, of uit RSS-koppen die de AI-stem letterlijk voorleest (zonder taalmodel, dus niets verzonnen). Geen actueel nieuws: tot het tijdstip elke 30 s opnieuw geprobeerd, anders het hele blok overgeslagen met een waarschuwing. Testknop laat zien wat er nu zou spelen. Live getest: blok startte op de seconde, opener → bulletin → sluiter |

## Ronde 3 — Koppelingen en reclame

| Status | Onderdeel |
|---|---|
| ✅ | Externe muziekplanning: logs importeren met import-profielen, bibliotheek exporteren |
| ✅ | Database-bibliotheken (ODBC) en metadata-opzoeking |
| ✅ | Reclamebeheer: adverteerders, campagnes, blokken, bewijs van uitzending |
| ✅ | Hardware-bediening: MIDI (knoppen, faders, lampjes), seriële poort (GPIO-kaarten, relais) en Livewire GPIO (LWRP) koppelen aan de commandobus; lampjes voor microfoon, op de lucht en automatisch (venster Hardware) |
| 🟡 | Ember+ (Lawo, LUCI Studio e.a.): knoppen, lampjes en boom-verkenner staan, adressen op nummer of op naam; nog testen tegen een echt apparaat. Mengtafel-specifieke koppelingen open |
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
