# Functiedekking en plan naar een volwaardige playout

*Opgesteld 12 november 2026.*

## Waar deze lijst vandaan komt

De lijst is opgesteld als **functielijst**. Bronnen:

- de openbare gebruikershandleiding van een volwaardig commercieel playout-pakket (versie 4.x);
- de namen van de hulpprogramma's in de installatiemap van dat pakket.

Er is **niets gedecompileerd of uit programmacode gehaald**. De licentie van dat pakket verbiedt reverse-engineeren (art. 5.2). Dat past ook bij onze eigen clean-room-afspraak.

Daaruit volgt:

- Alles hieronder is in eigen woorden beschreven. We nemen geen namen, schermen, teksten of bestandsformaten over.
- Waar we iets inlezen voor overstappers, gebruiken we alleen openbare of open formaten (M3U, PLS, XSPF, BWF/CART, ID3).

**Legenda:**

- ✅ REA-Radio-DJ heeft het;
- 🟡 gedeeltelijk, of nog niet getest;
- ⬜ ontbreekt;
- ➖ bewust niet (zie onderaan);
- *(na te gaan)*: de stand is niet zeker, eerst in de code of de studio controleren.

---

## 1. Geluidsengine en formaten

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | 24/7-bedrijf, 32-bit float-verwerking (BASS) | engine los van de studio, met een watchdog |
| ✅/🟡 | WASAPI, DirectSound en ASIO per uitgang, buffer en samplerate | ASIO gebouwd; een stuurprogramma dat niet start, is nog niet goed opgevangen |
| ✅ | Afspelen van MP3, WAV, OGG, FLAC, AAC/M4A, Opus en meer | via BASS met plug-ins |
| 🟡 | Broadcast Wave: BEXT- en CART-chunks lezen en schrijven, RF64 | lezen is er; schrijven en RF64 *(na te gaan)* |
| ⬜ | Exotische formaten: MIDI, MOD, audio-cd als bron | lage prioriteit |
| ✅ | ReplayGain en loudness (LUFS), automatische BPM, toonsoort | in de bibliotheek |
| ✅ | Tags lezen en schrijven (ID3, Vorbis, APE, MP4), hoezen | in het detailpaneel en in het bestand |
| ✅ | Metagegevens in een apart bestand naast de audio | sidecar-bestanden |

## 2. Mengpaneel en routering

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | Kanaalstrip: gain, balans, EQ, compressor, AGC, dempen, naar de zender, opnemen | Mixer › ⚙ |
| ✅ | VST-effecten per kanaal | drie plekken per kanaal |
| ✅ | Uitgang per kanaal; werkwijze "mengtafel" of "software" | Instellingen › Geluid |
| ⬜ | **Vrij aantal in- en uitgangskanalen en subgroepen** (groepsbus, som van een paar spelers, vrije luidsprekertoewijzing) | onze kanalen liggen vast |
| ⬜ | **Mengpanelprofielen en presets** (hele mixerinstelling of alleen EQ/gain met één klik wisselen) | ontbreekt |
| 🟡 | Spreken over muziek (microfoon dempt de muziek automatisch) | *(na te gaan)* |
| ⬜ | **Bewaking van de geluidskaart**: waarschuwen of terugzetten als een ander programma het volume of de demping van het apparaat verandert | ontbreekt |
| ✅ | Gesynchroniseerde PFL, en een PFL-speler die vooruit kan luisteren | PFL-speler |

## 3. Spelers

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | Automatiseringsdecks met log | Live-decks |
| ✅/🟡 | DJ-spelers: golfvorm, hot cues, loops, tempo en pitch, achteruit, backspin, effecten | DJ-modus gebouwd, nog niet beluisterd |
| ✅ | Standby-spelers met stapelmodus | twee standby-spelers |
| ✅ | Twee cartwalls met pagina's, meerdere carts tegelijk of na elkaar, plus een sampler | Cartwall 1 en 2, Sampler (DJ) |
| ✅ | Overlayspeler boven het programma | overlays |
| ✅ | Externe stream bewaken en bij signaal het programma onderbreken of overleggen | commandobus, ronde 2a |

## 4. Cues, golfvorm en overgangen

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | Cue-editor: in, intro, mix, uit, fade, hooks; automatisch berekend | cue-editor |
| ✅/🟡 | Gebeurtenissen op een plek in een nummer (volumeomhullende, opdracht) | in de cue-editor |
| ✅ | Mengregels per soort item (muziek, jingle, spraak) | *(fijnheid na te gaan)* |
| ⬜ | **Overgangseditor met twee sporen**: de overgang van nummer A naar B visueel slepen, een voice-over ertussen leggen en direct inspreken, met automatische demping van de muziek | **grootste ontbrekende stuk in de bediening** |
| ✅ | Uitlichtvenster voor de presentator: huidige en volgende titel met tijden | Live-balk en Itemgegevens *(een vrije presentatortekst per item na te gaan)* |

## 5. Bibliotheek

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | Mappen als bibliotheek met automatisch bijwerken, ook op shares | Bibliotheek › Beheer |
| ✅ | Externe database als bron | Databronnen |
| 🟡 | Een afspeellijst (M3U, PLS, XSPF) als bibliotheek of als één doorlopend item | importeren *(als vaste bron na te gaan)* |
| ⬜ | **Centrale bibliotheekserver voor meerdere studio's**: één plek scant en meet, de studio's vragen op en halen het bestand zo nodig over | staat op de werklijst (stap 7) |
| ✅ | Snel zoeken met filters (jaar, BPM, energie, stemming en meer) | zoektaal met chips |
| 🟡 | "Vergelijkbare nummers" zoeken | via toonsoort/BPM *(echte gelijkenis ontbreekt)* |
| ✅ | Overstappen: cues en tags uit andere playouts inlezen | waar het om open formaten gaat |
| 🟡 | Exporteren van de bibliotheek naar CSV of een database | *(na te gaan)* |

## 6. Automatisering en planning

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | Klokken en scriptregels (rotatie, scheiding, niet-na-regels) | planner |
| ✅ | Agenda met vaste of zachte starttijd, overlay of nieuw programma, herinneringen en alarmen | planner en commando's |
| ✅ | Live-assist ↔ automatisch; de automatisering loopt mee onder de DJ | |
| ✅ | Containers (groep items als één regel) | klembord → container |
| ✅ | Overlay-afspeellijsten (reclame, nieuws, verkeer) op vaste tijden | |
| 🟡 | **Logs inlezen uit externe muziekplanners en traffic-systemen** | er is een log-import; meer formaten en een koppelprofiel per leverancier volgen |
| ✅ | Globale speelgeschiedenis voor rotatie | as-run |

## 7. Reclame en nieuws

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | Adverteerders, campagnes, blokken, uitzendbewijs per klant | Reclame |
| ⬜ | **Meerdere regio's**: per regio andere reclame in hetzelfde blok (aparte uitgang of stream) | ontbreekt |
| ✅ | Nieuwsblokken, RSS en berichtencentrum; AI-nieuws en AI-weer | eigen uitbreiding |
| ⬜ | Reclamebeheer als losse werkplek voor de reclameafdeling (zonder de studio) | kan later als webpagina |

## 8. Uitzenden en opnemen

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | Meerdere streamservers tegelijk (Icecast, SHOUTcast v1/v2), MP3, AAC, Ogg, Opus, FLAC, HLS | |
| ✅ | Encoderprofielen | |
| ✅ | Programma per uur opnemen met bewaartermijn; microfoon opnemen | |
| 🟡 | **Opnemen met signaaldetectie** (alleen opnemen bij geluid, bijv. alleen de presentatorpraat als luistercontrole) | *(na te gaan)* |
| ✅ | Direct inspreken naar de bibliotheek (voicetrack) | |

## 9. Bediening op afstand en hardware

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | Commando's via HTTP, TCP, UDP en OSC; sneltoetsen | commandobus |
| ✅ | MIDI met "leren", serieel, Livewire, Ember+, Mackie Control | nog niet tegen echte apparaten getest |
| 🟡 | **Commandotaal met variabelen, voorwaarden en functies** (titel, tijd, als-dan, tekst opmaken) in alle gebeurtenissen | basiscommando's zijn er; variabelen en voorwaarden *(na te gaan)* |
| ⬜ | Commando's vanaf de opdrachtregel naar de engine sturen (voor andere software) | klein: een `rea-cmd.exe` op de bestaande TCP-poort |
| ⬜ | USB-GPIO-kaarten, D&R Airence/Webstation | op de werklijst (stap 8) |
| ➖ | Joystickpoort | verouderd |

## 10. Meerdere studio's en meekijken

| | Functie | Stand bij ons |
|---|---|---|
| ⬜ | **Studio's elkaar laten bedienen; hoofd- en reservestudio met overname** | stap 9 |
| ⬜ | **Meekijkscherm**: vorige/huidige/volgende, tijden en de stand van alle spelers op een tweede plek of op afstand | samen met de remote-app |
| ⬜ | Voicetracking op afstand (thuis inspreken) | stap 9 |

## 11. Bedrijfszekerheid, logging en rapportage

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | Watchdog met herstart, stiltedetectie, as-run, uitgebreide logging | |
| ✅ | Uitzendstatistiek per dag, week en maand | |
| ⬜ | **Vrije analyse** (draaitabel en grafieken over de hele geschiedenis) | uitbreiding van de statistiek |
| ✅ | Rapporten auteursrechten (CSV en PDF) | |
| 🟡 | Afdrukken of exporteren van log en planning (PDF/HTML) | rapporten ja; log en planning *(na te gaan)* |

## 12. Gebruikersomgeving

| | Functie | Stand bij ons |
|---|---|---|
| ✅ | Dockbare vensters, meerdere schermen, werkbladen | |
| ✅ | Thema's (donker, licht), hoge DPI, aanraakmodus | |
| ✅ | Gebruikers, rollen en rechten met pincode, studio vergrendelen | |
| ✅ | Klok met kwartier- en uuraftelling | |
| ⬜ | Stationsbeeld: logo, foto van de presentator, live spectrum | klein |
| ⬜ | Ingebouwd webpaneel (eigen pagina's, luisteraarsinteractie) | WebView, klein |

## Wat REA-Radio-DJ al heeft en dat pakket niet

- AI-presentator met lokaal taalmodel en eigen stem, en een spraakcontrole.
- AI-nieuws en AI-weerbericht.
- VoIP en SIP met twee mix-minuskanalen.
- DJ-modus met beatsync, automix en sampler.
- Wensenlijst uit Spotify.
- Bibliotheek met kwaliteitscontrole, herkenning van verplaatste nummers en ongedaan maken.
- Open bron (GPL-3.0); geen licentiesleutel of dongle.

---

## Plan

We bouwen **niet na**, maar dekken dezelfde functies af met onze eigen architectuur, die al staat:

- engine (ASP.NET + BASS) met API en commandobus;
- studio (Avalonia) als client;
- SQLite als stationsdatabase.

Het plan heeft vier fases. Elke fase levert een werkende, geteste versie op.

### Fase A — Wat er staat bedrijfszeker maken (eerst)

Zonder dit heeft uitbreiden geen zin.

- Luistertests:
  - DJ-modus;
  - AI-nieuws op de zender;
  - opnemen met de bewaartermijn.
- Testen tegen echte apparatuur:
  - ASIO-stuurprogramma's, en een stuurprogramma dat niet start netjes opvangen;
  - SIP bij de aanbieder;
  - Ember+ tegen LUCI/Lawo;
  - Mackie Control met een echt paneel.
- De punten *(na te gaan)* uit de tabellen hierboven nalopen en de stand vastleggen.
- De cosmetische ronde over de hele studio.

### Fase B — De grote functionele gaten

Op volgorde van belang voor Radio Aa en Hunze:

1. **Overgangseditor met twee sporen.** Je ziet het einde van A en het begin van B onder elkaar en sleept B. Daarbij:
   - een voice-over-spoor, met inspreken op de plek zelf;
   - automatische demping (een volumeomhullende op A en B);
   - opslaan als de overgang van dit paar of als eigen cue.
   - Bouwt voort op de cue-editor en de gebeurtenissen in een nummer.
2. **Mengpaneel flexibel:**
   - vrij aantal kanalen;
   - subgroepen;
   - profielen en presets (opslaan, laden, één klik);
   - de microfoon dempt de muziek (als dat er nog niet is).
3. **Reclame per regio.** Een blok krijgt per regio een eigen inhoud op een eigen uitgang of stream, met het uitzendbewijs per regio.
4. **Commandotaal uitbreiden.** Variabelen (titel, artiest, tijd, speler), voorwaarden en een paar tekstfuncties, in alle gebeurtenissen:
   - planner;
   - cue-gebeurtenissen;
   - MIDI, OSC en serieel;
   - stream-gebeurtenissen.
   - Plus een klein opdrachtregelprogramma om commando's te sturen.
5. **Opnemen met signaaldetectie**, en de geluidskaart bewaken op volume en demping.

### Fase C — Meerdere plekken

1. **Centrale bibliotheekdienst.** De engine kan als bibliotheekserver draaien. Andere engines vragen hun bibliotheek daar op en halen het bestand over als het lokaal niet bereikbaar is.
2. **Meerdere studio's.** Een hoofd- en reservestudio met hartslag en automatische overname, en bediening van de andere studio.
3. **Meekijkscherm en remote-app.** Eén weergave voor drie plekken: een tweede plek in de studio, de tablet (bedienen) en de telefoon (bewaken).
4. **Voicetracking op afstand.** Thuis inspreken in de overgangseditor; de opname gaat naar de engine.

### Fase D — Afwerking

- Logs van externe muziekplanners en traffic-systemen. Eerst de formaten die het station echt gebruikt, daarna per leverancier een koppelprofiel.
- D&R Airence/Webstation en USB-GPIO via de algemene hardwarelaag (alleen met het openbare protocol van de fabrikant).
- Vrije statistiekanalyse (draaitabel en grafieken).
- Stationsbeeld, webpaneel, en log en planning afdrukken of exporteren.
- BWF/CART-schrijven en RF64 controleren.
- De handleiding voor de studio en voor de beheerder.

### Bewust niet

| Wat | Waarom |
|---|---|
| Windows Media Server-streaming, joystickpoort | verouderd |
| Cd's rippen en branden | staat al onder "Wat niet op de lijst staat" |
| Registratie aan pc, harde schijf of USB-stick | REA-Radio-DJ is open bron |
| Eigen afspeellijst- of databaseformaat van dat pakket | clean-room: alleen open formaten |
