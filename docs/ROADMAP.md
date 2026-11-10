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
| ⬜ | Remote-app: Android-tablet (bedienen) en telefoon (bewaken met meldingen), volgens het layout-ontwerp; rechten per gebruiker |

## Audio en techniek

| Status | Onderdeel |
|---|---|
| ⬜ | ASIO-uitvoer |
| ⬜ | Meerdere streamservers en codecs (AAC, Opus), HLS |
| ⬜ | Opnemen (programma, mic) en automatische opname |
| ⬜ | Kanaalstrip per mengkanaal: gain, balans, equalizer, compressor, automatische niveauregeling (AGC) en vrije plekken voor VST-effecten; per kanaal aan/uit, dempen, naar de zender sturen en opnemen; een masterfader met begrenzer en een knop voor praten-over-muziek |
| ⬜ | Watchdog en herstel na een crash |
| ⬜ | Mixpunten per nummer met overgangstypes per categorie (knippen, faden, over de intro), bovenop de overgangseditor |
| ⬜ | OSC uitsturen (status naar mengtafel of lichtsturing), naast het bestaande OSC-commandokanaal |

## Studio-layout moderniseren

Volgens het ontwerp "REA-Radio-DJ Studio-layout" (1, 2 en 3 schermen, tablet en telefoon). Ontwerpregels: de
aan-de-luchtbalk staat altijd bovenaan; één hoofdactie per gebied; een toestand nooit alleen met kleur; raster van
8 px met knoppen van minstens 44 px; werkruimtes als vaste navigatie, vensters blijven vrij te docken.

| Status | Onderdeel |
|---|---|
| 🟡 | Stijlbasis: kleurenpalet en knopstijlen van het ontwerp staan als bronnen in de app en zijn gebruikt in de bovenbalken; de vensters zelf volgen per onderdeel hieronder |
| 🟡 | App-balk: naam, menu, werkruimtes Live en Automatisering, indelingkeuze en enginestatus staan; werkruimtes Productie, Planning en Rapportage nog open |
| ✅ | Aan-de-luchtbalk nieuw: titel met voortgang, intro- en mixmarkering, grote resterende tijd, Volgende als enige gevulde knop, Stop apart en 1 s vasthouden (F9 stopt direct); onder 1900 px breed krijgen de knoppen een eigen rij |
| ⬜ | Spelers en cartwall in de nieuwe stijl: toestand als woord en vorm (leeg, klaar, speelt, laatste 10 s), cartknoppen van 96 px |
| ⬜ | Programmalog in de nieuwe stijl: tijd, kleurstreep per soort, titel met ondertitel, lengte |
| ⬜ | Responsive: breder dan 1600 px naast elkaar, 1100-1600 px cartwall onder de spelers, smaller alles in één kolom |
| ⬜ | Indelingen voor 2 en 3 schermen (Uitzending, Productie, Cartwalls + mixer); onthouden per indeling, terugvallen naar scherm 1 als een scherm wegvalt |
| ⬜ | Overige vensters (planner, bibliotheek, mixer, beheer) nalopen op de nieuwe stijl |

## DJ-modus

Een eigen werkruimte in dezelfde studio om met de hand te mixen: twee of vier decks, een mengpaneel en de
bibliotheek eronder. Eigen ontwerp in de stijl van de studio; geen skins, merken of beelden van andere dj-pakketten.
De DJ-modus speelt op een eigen mengkanaal, zodat de automatisering kan doorlopen of juist wijkt.

| Status | Onderdeel |
|---|---|
| 🟡 | Analyse per nummer: BPM en beatgrid worden gemeten zodra een nummer voor het eerst op een deck komt en bewaard (`dj.json` in de datamap). Nog open: toonsoort, ReplayGain, en meten bij het inlezen van de bibliotheek |
| ✅ | Deck-engine: vier decks op een eigen mengkanaal (DJ); laden, start/pauze, cue, springen, tempo ±8/12/50 % met of zonder toonhoogtebehoud, even aanduwen of afremmen, sync op tempo en beat (ook half en dubbel tempo). De tafel staat standaard niet op de lucht |
| 🟡 | Mengpaneel: gain, 3-bands EQ met kill, filter (laag- en hoogdoorlaat op één knop), kanaalfaders met niveaumeter, crossfader vloeiend of scherp, voorluisteren per deck. Nog open: cue-mix op de koptelefoon en een begrenzer op de DJ-uitgang |
| 🟡 | Werkruimte DJ in de app-balk: 2 of 4 decks met golfvorm van het hele nummer, tellers, BPM en tempo, mengpaneel in het midden, bibliotheek eronder (slepen naar een deck). Nog open: de twee doorlopende golfvormen boven elkaar met beatstrepen, en de toonsoort |
| ✅ | Overnemen van de automatisering: bij het openen van de werkruimte DJ speelt deck A mee met wat op de zender is (zelfde nummer, zelfde plek, onhoorbaar zolang de tafel niet op de lucht staat). DJ op de lucht stopt de automatisering op dat moment, deck A loopt door. Volgt ook als de automatisering naar het volgende nummer gaat; wie deck A zelf bedient, neemt het over |
| 🟡 | Hotcues (8 per nummer, bewaard) en loops (in/uit, aantal beats, halveren/verdubbelen) staan. Nog open: loop-roll en quantize op de beat |
| ⬜ | Effecten per deck (flanger, reverb, echo, filter) met sterkte en lengte op de beat |
| ⬜ | Sampler: de bestaande cartwall als pads in de DJ-modus |
| ⬜ | Automix in de DJ-modus: lijst afspelen met overgangstype (knippen, faden, tempo-overgang); mixpunten per nummer |
| ⬜ | Bediening: decks, mengpaneel en pads op de commandobus, dus ook via MIDI-controllers en sneltoetsen |
| ⬜ | Stems (zang, instrumenten, bas, drums) met een open scheidingsmodel op de GPU, vooraf berekend per nummer |
| ⬜ | Opname van de mix en tempo-overgang als overgangstype in de automatisering |

## Verbindingen (codec)

Een eigen venster voor live-verbindingen met verslaggevers, andere studio's en bellers, met de functies van een
audiocodec. Alleen open protocollen en codecs; eigen ontwerp. Elke verbinding komt als bron op een mengkanaal
(bijv. VoIP) en krijgt een terugkanaal zonder zichzelf (mix-minus).

| Status | Onderdeel |
|---|---|
| ⬜ | Mengkanaal VoIP/extern in de mixer met eigen fader, PFL en mix-minus-terugkanaal |
| ⬜ | RTP in twee richtingen: G.711, G.722, Opus en lineair PCM (L16/L24), jitterbuffer met meting (jitter, verlies, kwaliteit) |
| ⬜ | SIP volgens EBU N/ACIP: bellen en gebeld worden, registreren bij een SIP-server, automatisch of handmatig aannemen |
| ⬜ | Venster Verbindingen: stationslijst (adresboek met protocol, codec, bitrate), verbinden/ophangen/aannemen, niveaumeters in en uit, verbindingstijd, kwaliteit, waarschuwingen |
| ⬜ | Presets per station en meerdere verbindingen tegelijk, elk op een eigen mengkanaal |
| ⬜ | Betrouwbaarheid: verbinding over twee netwerken tegelijk, RTP over SRT, automatisch opnieuw verbinden |
| ⬜ | Icecast/SHOUTcast-bron en RTP-multicast als verbindingstype; NDI- of ASIO/WASAPI-ingang als bron (bijv. LUCI Studio of een dj-programma) |
| ⬜ | Bediening op afstand: verbinden en status op de commandobus (knoppen, lampjes, Ember+, OSC) |
| ⬜ | Tekstberichten naar de overkant en opname van een verbinding |

## Instellingen en doorlichting

Het venster Instellingen (menu Studio) bundelt wat eerst alleen in `appsettings.json` kon. Hieronder wat een
doorlichting van het hele programma verder opleverde: wat een playoutsysteem nodig heeft en hier nog ontbreekt of
alleen in een bestand in te stellen is. Eigen opzet en eigen benamingen.

| Status | Onderdeel |
|---|---|
| ✅ | Venster Instellingen: station (naam, mappen, automatisch starten), geluidskaarten en microfoon, mixen en loudness, drempels voor automatische cuepunten, rotatie, stream, bediening op afstand (adres, poorten, wachtwoord) en deze studiocomputer. Wachtwoorden blijven in de engine |
| ⬜ | Geluid uitgebreid: uitgang per kanaal kiezen (spelers, cartwalls, overlays, DJ, VoIP elk naar een eigen kaart of kanaalpaar), meer ingangen dan één microfoon (lijn-in als mengkanaal), testtoon per uitgang, buffer en samplerate instelbaar |
| 🟡 | Stiltedetectie: is de zender een instelbare tijd stiller dan een drempel, dan een alarm in Berichten en naar keuze de automatisering starten (Instellingen › Bewaking; standaard 20 s onder -45 dB). Nog open: een noodband spelen |
| ⬜ | Gebeurtenissen koppelen aan commando's: bij start en einde van een item, microfoon open of dicht, stream weg, fout, engine gestart (bijv. een lampje, een bericht of een webadres aanroepen) |
| ⬜ | Bibliotheek-instellingen: meer dan twee mappen, mappen automatisch opnieuw inlezen op een vast moment of bij wijziging, bestanden die elders staan (vervangende locatie), tags lezen en terugschrijven, hoezen |
| ⬜ | Overgangen per soort item: eigen overlap en fade voor muziek, jingles, reclame, nieuws en stem; items korter dan een aantal seconden niet faden |
| ⬜ | Titelupdates naar de stream: eigen opmaak, soorten items uitsluiten (jingles, reclame), vertraging gelijk aan de streambuffer; ook naar een tekstbestand of webadres (RDS, website) |
| ⬜ | Logbestanden: programmalog als tekst met eigen opmaak, bewaartermijn, soorten uitsluiten; systeemlog van de engine in de studio in te zien met niveau (gewoon of uitgebreid) |
| ⬜ | Opname-instellingen (hoort bij Opnemen): map, bestandsnaam met datum en uur, formaat, bewaartermijn, automatisch per uur |
| ⬜ | Uiterlijk: lettergrootte, aanraakmodus (grotere knoppen), licht thema, kleuren per soort item op één plek; tooltips aan of uit |
| ⬜ | Studio vergrendelen na een instelbare tijd en bij Gebruikers en rechten: instellingen alleen voor beheerders |
| ⬜ | Reservekopie: alle instellingen, klokken, planning en cartwalls in één bestand opslaan en terugzetten; ook om een tweede studio in te richten |
| ⬜ | Controle op nieuwe versies en een pagina Over met versies van engine, studio en bibliotheken |
| ⬜ | Hardware erbij: GPIO-kaarten via USB (bijv. Velleman K8055, IO-Warrior), joystick-/gamepoort als knoppenbak, tweede Ember+- en Livewire-apparaat als reserve, en mengtafels van D&R (Airlite, Airence, Webstation), AxelTech Oxygen en Wheatstone (WheatNet-Blade), elk met het protocol dat de fabrikant openbaar maakt |
| ⬜ | Bediening via een webadres per knop (REST) en een UDP-luisterpoort voor eenvoudige apparaten, naast TCP en OSC |
| ⬜ | Sneltoetsen die ook werken als de studio niet op de voorgrond staat (naar keuze) |
| ⬜ | Eigen knoppen: een rij vrij te vullen knoppen in de studio, elk met een naam en een of meer commando's van de commandobus |
| ⬜ | Lijn-in doorzetten: een ingang (cd-speler, tweede studio, satelliet) met één knop op de zender, met in- en uitfade; ook als item in de planning |
| ⬜ | Direct opnemen: een knop die de microfoon of een ingang opneemt naar een nieuw item in de bibliotheek |
| ⬜ | Verkenner in de studio: door mappen bladeren en een bestand dat niet in de bibliotheek staat voorluisteren of in de log zetten |
| ⬜ | Inrichthulp bij de eerste start: stap voor stap station, mappen, geluidskaarten en stream kiezen, met een testtoon |
| ⬜ | Informatievensters: itemgegevens van wat speelt (tags, hoes, laatst gedraaid), stationsinformatie en een webpagina naar keuze in een venster |
| ⬜ | Regiekamer-venster: het weer voor de eigen plaats, nieuwskoppen (RSS) en berichten voor de presentator naast elkaar in beeld; de gegevens zijn er al voor de AI-presentator (Open-Meteo, zonder sleutel) |
