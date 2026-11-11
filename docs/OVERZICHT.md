# Overzicht: wat er staat en wat nog moet

Stand van 10 november 2026. Dit overzicht is opgebouwd uit de werklijst ([WERKLIJST.md](WERKLIJST.md)), het rondeplan ([ROADMAP.md](ROADMAP.md)) en alle commits sinds de start. Waar die twee elkaar tegenspreken, is de werklijst leidend.

Legenda: ✅ klaar en getest · 🟡 werkt, maar een deel of de echte test ontbreekt · ⬜ nog te bouwen

## Waar we begonnen

Op 8 november 2026 begon REA-Radio-DJ als proof of concept: een eigen playoutsysteem voor Radio Aa en Hunze, open source (GPL-3.0), in C# met de BASS-geluidsbibliotheek. Het bestaat uit twee delen:

- een **engine** die het geluid en de automatisering doet;
- een **studio** waarmee je de engine bedient. Die kan op 1 tot 4 schermen staan.

Bij de start zijn de volgende pakketten bekeken als **controlelijst van functies**:

- een professioneel playoutpakket en RoboDJ (automatisering, planning, studio);
- VirtualDJ (dj-tafel);
- LUCI (codec en live-verbindingen).

Van die pakketten is niets overgenomen: geen namen, schermen, teksten, skins of bestandsformaten. Er is niets uit gedecompileerd. Er worden alleen open protocollen en codecs gebruikt.

Bewust **niet** op de lijst:

- de eigen codec van LUCI (ULCC) en de licentie en activatie daarvan;
- inloggen op of doorstreamen van de muziekcatalogus van een dj-pakket;
- cd's rippen en branden.

In drie dagen zijn er 51 commits gemaakt. Er zijn 226 geautomatiseerde tests, en die slagen allemaal.

## Gerealiseerd

### Automatisering en planning

|  | Onderdeel |
| --- | --- |
| ✅ | Engine als achtergronddienst met tray-icoon, REST- en WebSocket-koppeling |
| ✅ | Bibliotheek met automatische cuepunten en LUFS-meting per nummer, rotatieregels |
| ✅ | Klokken, weekrooster, vaste tijden; planner met dag- en weekoverzicht |
| ✅ | Programmalog die een herstart overleeft (ook na een harde crash: zelfde items, zelfde volgorde) |
| ✅ | Overgangseditor: twee golfvormen, startpunt slepen, volumelijnen, jingle als insert, beluisteren op PFL |
| ✅ | Nieuwsblokken op een vaste tijd (opener, bulletin uit een map, categorie of RSS, sluiter) |
| 🟡 | AI-nieuws (landelijk + regionaal, herschreven door het lokale taalmodel) en AI-weerbericht als nieuwsblok, met eigen stem en tune; nog niet beluisterd op de zender |
| ✅ | Reclame: adverteerders, campagnes, blokken, bewijs van uitzending |
| ✅ | Logs importeren uit een externe muziekplanner, bibliotheek exporteren, ODBC-bronnen, metadata opzoeken |
| ✅ | Spotify-wensenlijst (alleen gegevens, nooit audio) gekoppeld aan de bibliotheek |
| 🟡 | Muziekrapport voor Buma/Stemra en Sena staat; de kolomindeling moet nog op het officiële sjabloon |

### Studio

|  | Onderdeel |
| --- | --- |
| ✅ | Dockbare vensters, indelingen per scherm |
| ✅ | Standby- en PFL-spelers met golfvorm en grote teller |
| ✅ | Twee cartwalls; Live-decks A/B/C met eigen kleuren |
| ✅ | Aftelbalk, introteller, studioklok (studio, analoog, digitaal) |
| ✅ | Nieuwe app-balk en aan-de-luchtbalk volgens het eigen layoutontwerp |
| ✅ | Venster Instellingen met zeven tabbladen (station, geluid, mixen, rotatie, stream, bediening, systeem) |
| ✅ | Systeemlog van de engine in te zien in de studio |
| ✅ | Voicetracking in drie stappen (start, volgende, stop) |

### AI-presentator

|  | Onderdeel |
| --- | --- |
| ✅ | Higgs TTS 3 op de GPU (in WSL), Windows-spraak, ElevenLabs en Azure als reservestemmen |
| ✅ | Taalmodel naar keuze (Ollama, LM Studio of online), persona's per dagdeel |
| ✅ | Script goedkeuren of bewerken voor het inspreken |
| ✅ | Muziekbed onder de break, talk-up over de intro |
| ✅ | Weer, nieuwskoppen en berichten als inhoud van een break |
| ✅ | Elke break wordt teruggeluisterd met Whisper en vergeleken met het script |
| ✅ | Stem klonen alleen met vastgelegde toestemming |

### Geluid en mengpaneel

|  | Onderdeel |
| --- | --- |
| ✅ | Mixer met ducking (praten over muziek) en mengkanalen voor automatisering, standby, cartwall, overlays, DJ, microfoon en PFL |
| ✅ | Elk mengkanaal kan naar een eigen Windows-geluidskaart of kanaalpaar, met een testtoon per uitgang |
| ✅ | Ingangen als mengkanaal (meer microfoons, lijn-in), met een ZENDER-knop |
| ✅ | Kanaalstrip per kanaal: gain, balans, 3-bands EQ, compressor, automatische niveauregeling, naar de zender |
| ✅ | Ingebouwd effect per fader: galm, echo, chorus, flanger, telefoon, stem lager of hoger |
| ✅ | Opnemen per kanaal (REC-knop) naar WAV. De opname wordt genomen na de kanaalstrip en voor de fader |
| ✅ | Master: eigen look-ahead-begrenzer en EBU R128-loudnessmeter (LUFS kort en geïntegreerd, true peak) |
| ✅ | Icecast-stream (MP3) |
| 🟡 | ASIO-uitvoer met samplerate en buffer: gebouwd en getest met Realtek ASIO, maar nog niet beluisterd en niet getest met een studiokaart |

### Bedrijfszekerheid

|  | Onderdeel |
| --- | --- |
| ✅ | Stiltedetectie met alarm, en daarna de automatisering starten of iets uit een noodcategorie spelen |
| ✅ | Watchdog: na een crash is de engine binnen 1,4 s terug, met hetzelfde nummer op dezelfde plek |
| ✅ | Reservekopie en terugzetten van alle instellingen, klokken, planning, cartwalls en hardware |
| 🟡 | Engine starten bij aanmelden: aan en uit zetten is getest, een echte herstart van de pc nog niet |

### Bediening en hardware

|  | Onderdeel |
| --- | --- |
| ✅ | Commandobus via HTTP, TCP en OSC; sneltoetsen; scriptregels; externe streams bewaken |
| ✅ | MIDI (knoppen, faders, lampjes), seriële GPIO en relais, Livewire GPIO |
| 🟡 | Ember+ (Lawo, LUCI Studio): gebouwd, maar nog niet getest tegen een echt apparaat |

### DJ-modus

|  | Onderdeel |
| --- | --- |
| ✅ | Vier decks op een eigen mengkanaal |
| ✅ | Tempo met en zonder toonhoogtebehoud; sync op tempo en beat |
| ✅ | Overnemen van de automatisering zonder gat |
| 🟡 | BPM en beatgrid worden gemeten bij het eerste laden; toonsoort en ReplayGain ontbreken nog |
| 🟡 | Mengpaneel met EQ en kill, filter en crossfader; cue-mix op de koptelefoon en een begrenzer ontbreken nog |
| 🟡 | Hotcues en loops; loop-roll en quantize ontbreken nog |
| 🟡 | Alles werkt, maar is nog niet beluisterd |

## Nog te realiseren

Gerangschikt naar de stappen van de werklijst. De proof of concept is af na stap 5.

### Stap 2 — Geluid (bijna af)

- ⬜ Plekken voor VST-effecten per kanaal
- ⬜ Echte tests (alleen met jouw toestemming):
    - een lijn-in;
    - ASIO beluisteren;
    - kanaalpaar 3/4 op een meerkanaalskaart.

### Stap 3 — Eén stijl in de hele studio

- ⬜ Spelers, cartwall en programmalog in de nieuwe stijl
- ⬜ Werkruimtes Productie, Planning en Rapportage
- ⬜ Indelingen voor 1 tot 4 schermen en een aanraakmodus
- ⬜ Overige vensters nalopen (planner, bibliotheek, mixer, beheer)
- ⬜ Uiterlijk instelbaar: lettergrootte, licht thema, kleuren per soort item
- ⬜ Nieuwe vensters:
    - eigen knoppen;
    - regiekamervenster (weer, nieuws, berichten);
    - itemgegevens;
    - verkenner.

### Stap 4 — DJ-modus afmaken

- ⬜ Luistertest en bijstellen
- ⬜ Twee doorlopende golfvormen met beatstrepen
- ⬜ Toonsoort; BPM, toonsoort en ReplayGain al bij het inlezen
- ⬜ Quantize, loop-roll, cue-mix en een begrenzer op de DJ-uitgang
- ⬜ Effecten per deck op de beat
- ⬜ Sampler-pads (de cartwall)
- ⬜ Automix met overgangstypes; tempo-overgang in de automatisering
- ⬜ Hercules- en Pioneer-controller via de commandobus
- ⬜ Mix opnemen naar de bibliotheek. De opname per kanaal is er al; het moet nog in de bibliotheek komen
- ⬜ Draaiknoppen, alles passend op 1920 × 1080
- ⬜ Later: stems (zang, instrumenten, bas, drums)

### Stap 5 — Verbindingen (codec en VoIP)

Dit is het grootste open blok van de proof of concept.

- ⬜ VoIP-mengkanaal met mix-minus (terugkanaal zonder zichzelf)
- ⬜ RTP met Opus, G.722, G.711 en PCM, met een jitterbuffer
- ⬜ SIP volgens EBU N/ACIP: bellen, gebeld worden, registreren. LUCI Live verbindt hiermee op een open codec
- ⬜ Venster Verbindingen met stations en presets
- ⬜ Bediening en status via de commandobus
- ⬜ Later: SRT, twee netwerken tegelijk, opname van een verbinding

### Stap 6 tot en met 10 — Van proof of concept naar product

| Stap | Wat nog moet |
| --- | --- |
| 6 Opnemen en uitzenden | Programma en microfoon automatisch per uur opnemen, met een bewaartermijn<br>Direct opnemen naar de bibliotheek<br>Meer streamservers tegelijk; AAC, Opus en HLS<br>Titelupdates met eigen opmaak, ook naar RDS of de website<br>Programmalog als tekstbestand |
| 7 Bibliotheek en planning | Meer mappen en automatisch opnieuw inlezen<br>Tags terugschrijven, hoezen<br>Overgangen per soort item<br>Centrale bibliotheek<br>Rapportage op het officiële sjabloon |
| 8 Bediening en hardware | Gebeurtenissen koppelen aan commando's<br>Bediening via een webadres per knop en via UDP<br>OSC uitsturen<br>USB-GPIO<br>Mengtafels van D\&R, AxelTech en Wheatstone<br>Ember+ testen |
| 9 Gebruikers en studio's | Rollen en rechten, studio vergrendelen<br>Meerdere studio's met automatische overname<br>Voicetracking op afstand<br>Remote-app voor tablet en telefoon |
| 10 Afwerking | Inrichthulp bij de eerste start<br>Installatieprogramma, controle op nieuwe versies, pagina Over<br>Handleidingen |

## Vergeleken met de bekeken pakketten

| Gebied (zoals in de andere pakketten gezien) | Stand bij ons |
| --- | --- |
| Playout en automatisering: klokken, log, cartwalls, voicetracking, nieuws, reclame, rapportage | ✅ Grotendeels klaar. Het meeste werk zit nog in de stijl en in schermindelingen (stap 3) |
| Mengpaneel met kanaalstrips, effecten, master en uitgangen per kanaal | ✅ Klaar. Alleen VST ontbreekt |
| Opnemen | 🟡 Per kanaal klaar. Automatische opname per uur en opnemen naar de bibliotheek ontbreken |
| Streaming | 🟡 Eén Icecast-stream in MP3. Meerdere servers en codecs ontbreken |
| DJ-tafel (vier decks, sync, loops, hotcues) | 🟡 De basis staat. Effecten, sampler, automix, controllers en stems ontbreken |
| Codec en live-verbindingen | ⬜ Alleen het ontwerp staat. Nog niets gebouwd |
| Hardware en mengtafels | 🟡 MIDI, serieel, Livewire en Ember+ staan. Tafelspecifieke koppelingen ontbreken |
| Gebruikers, meerdere studio's, remote-app | ⬜ Nog niet begonnen |
| Iets wat de bekeken pakketten niet hebben | ✅ AI-presentator met eigen stem, Whisper-controle en persona's |

## Antwoorden van het station (10 november 2026)

Alle vragen zijn beantwoord. De gevolgen staan als werk in de [werklijst](WERKLIJST.md).

| Vraag | Antwoord |
|---|---|
| Wat bij stilte? | Een aparte reserve-afspeellijst, of willekeurig uit de muziekbibliotheek |
| Uitgangen naar de mengtafel | 4 tot 6, waarvan 2 voor VoIP |
| Software of mengtafel? | Beide: normaal mengt de tafel live, bij automatisering de software |
| VST nodig? | Ja, voor de proof of concept |
| Licht thema? | Ja, meerdere thema's, met afgeronde vormen, goede bedienbaarheid en kleuren |
| DJ-controller | Hercules en Pioneer, ook oudere typen |
| Automatisering onder de DJ | Onhoorbaar laten doorlopen, en duidelijk tonen met een oplichtende knop of veld |
| SIP-aanbieder | PortaOne-centrale, nu voip.sipnl.net; flexibel instelbaar |
| Bereikbaarheid van buiten | Externe telefonie via de SIP-server van voip.sipnl.net |
| Streamservers en formaten | Alle gangbare servers en formaten van bekende stream-encoders |
| Bewaren van uitzendingen | Instelbaar, normaal een jaar |
| Rapportagesjabloon | Geen voorbeeld; een eigen indeling. Voor reclame ook een los afspeelrapport als bewijs voor klanten en stichtingen, later te koppelen aan een eigen systeem |
| Hardware in de studio | D&R Airence en D&R Webstation; geschikt maken voor meerdere fabrikanten |

## Nog niet op echte apparatuur getest

- ASIO beluisteren, en ASIO met een studiokaart
- Kanaalpaar 3/4 op een meerkanaalskaart
- Een echte lijn-in
- De engine na een echte herstart van de pc
- Ember+ tegen LUCI Studio of een Lawo-tafel
- De DJ-modus beluisteren
