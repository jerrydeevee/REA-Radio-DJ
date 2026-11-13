# Functiedekking en plan naar een volwaardige playout

*Versie 2.2, 13 november 2026: onderdeel 17 en fase J toegevoegd. Versie 2.1 (13 november): onderdeel 16 en fase I. Versie 2 (12 november) Versie 1 was een eerste overzicht; deze versie is uitgewerkt per onderdeel, de open punten zijn in de code nagelopen, en het plan is uitgebreid met architectuur, opleveringen en controlepunten.*

## Waar deze lijst vandaan komt

De lijst is een **functielijst**, geen bouwtekening. Bronnen:

- de openbare gebruikershandleiding van een volwaardig commercieel playout-pakket (versie 4.x, 336 pagina's);
- de bestandsnamen in de installatiemap van dat pakket (welke hulpprogramma's en modules er zijn);
- voor onderdeel 14: de openbare helpsite van een mediabeheer- en workflowplatform voor video en tv (versie rond 2021–2022; de site wordt niet meer bijgehouden);
- voor onderdeel 15: de openbare artikelen op de supportsite van dezelfde leverancier, over zijn radio- en nieuwsproducten. Het meeste staat achter een klantenlogin; alleen de openbare artikelen zijn gelezen (13 november 2026).
- voor onderdeel 16: nog eens twaalf openbare artikelen van die supportsite (13 november 2026). Ze gaan over gebruikersbeheer, de gebruikershandleiding van het mediabeheer, de koppeling met een videomontageprogramma, ondertitels, databaseversies, geteste geluidskaarten, trainingen, certificaten, systeemaanbevelingen, Windows-instellingen, DCOM-beveiliging en de eisen voor eigen hosting. Het artikel over de technische opbouw bestaat alleen uit bijlagen (PDF); die zijn niet gedownload.
- voor onderdeel 17: het openbare helpcentrum van een andere leverancier van radio-playoutsoftware, de categorie over zijn nieuwste playoutversie (13 november 2026). Van de 92 artikelen zijn de titels van 73 bekeken en 26 gelezen; de rest zat achter "alles bekijken"-links.

Er is **niets gedecompileerd, uitgepakt of uit programmacode gehaald**. De licentie van dat pakket verbiedt reverse-engineeren, en het past bij onze eigen clean-room-afspraak. Daaruit volgt:

- Alles hieronder staat in eigen woorden. We nemen geen namen, schermen, teksten, commandonamen of bestandsformaten over.
- Waar we iets inlezen voor overstappers, gebruiken we alleen openbare of open formaten (M3U, PLS, XSPF, BWF/CART, ID3, CSV).
- "Rebuild" betekent hier: **dezelfde functies afdekken met onze eigen architectuur en ons eigen ontwerp**, niet het pakket namaken.

**Legenda:** ✅ REA-Radio-DJ heeft het · 🟡 gedeeltelijk of nog niet getest · ⬜ ontbreekt · ➖ bewust niet

Het belang staat in de kolom **Prio** voor Radio Aa en Hunze: **H** hoog, **M** middel, **L** laag.

---

## Deel 1 — Functielijst per onderdeel

### 1. Geluidsengine en formaten

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | 24/7-bedrijf, 32-bit float-verwerking | engine (BASS) los van de studio, met watchdog | — |
| 🟡 | WDM/WASAPI en ASIO 2.0, meerkanaalskaarten | ASIO per uitgang gebouwd; een stuurprogramma dat niet start wordt nog niet opgevangen met een terugval | H |
| ✅ | Afspelen van MP1/2/3, WAV, OGG, FLAC, AAC/M4A, Opus, WMA, APE, MPC, internetstreams | via BASS en plug-ins | — |
| ⬜ | Broadcast Wave: BEXT- en CART-gegevens lezen **en schrijven**, RF64 (wav boven 4 GB) | niet in de code gevonden | M |
| ✅ | ReplayGain (piek en waargenomen luidheid), LUFS, BPM, toonsoort | bij het inlezen | — |
| ✅ | Tags lezen en schrijven (ID3v1/v2, Vorbis, APE, MP4, RIFF), hoezen | detailpaneel; ook in het bestand | — |
| ✅ | Metagegevens in een apart bestand naast de audio (bron niet aanraken) | sidecar-bestanden | — |
| ⬜ | Opnemen/transcoderen via een vrij in te stellen opdrachtregel-encoder | wij hebben vaste encoders | L |
| ➖ | Audio-cd rippen en branden | zie "Bewust niet" | — |

### 2. Mengpaneel en routering

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Kanaalstrip: gain, balans, 10-bands EQ, compressor/begrenzer, automatische niveauregeling, dempen | Mixer › ⚙ | — |
| ✅ | VST-effecten per kanaal | drie plekken per kanaal | — |
| ✅ | Uitgang per kanaal; werkwijze "mengtafel" of "software" | Instellingen › Geluid | — |
| ✅ | Spreken over muziek: microfoon of overlay dempt de muziek | dempniveau per strip (talkover/ducking) | — |
| ⬜ | **Vrij aantal in- en uitgangskanalen** en **subgroepen** (som van een paar spelers, groepsbus, luidsprekertoewijzing per kanaal) | onze kanalen liggen vast | M |
| ⬜ | **Profielen met presets**: hele mengpanelinstelling of alleen EQ/gain/pan wisselen met één klik | ontbreekt | M |
| ⬜ | "Talk to user": een tweede spreekcircuit naar één bepaalde uitgang (bijv. de beller of de tweede studio), los van de zender | deels via de VoIP-terugweg | L |
| ⬜ | **Bewaking van de geluidskaart**: melden of terugzetten als Windows of een ander programma het volume of de demping van het apparaat verandert | ontbreekt | H |
| ⬜ | Inrichthulp voor het mengpaneel: in een paar stappen kanalen, uitgangen en routering kiezen | onze inrichthulp doet alleen de basis | L |
| ✅ | Gesynchroniseerde PFL op elke bron, plus een PFL-speler die vooruit luistert | PFL-speler | — |
| ⬜ | Volumeregeling van een externe tafel of kaart vanuit het programma (uitgangsniveau van het apparaat) | ontbreekt | L |

### 3. Spelers

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Automatiseringsdecks bij de log, met aftellen tot mix, uit en volgende | Live-decks | — |
| 🟡 | DJ-spelers: golfvorm, hot cues, loops, sampler, tempo/pitch met vaste toonhoogte, achteruit, backspin, effecten | DJ-modus gebouwd, luistertest nog te doen | M |
| ✅ | Standby-spelers met stapel (meerdere items achter elkaar) | twee standby-spelers | — |
| ⬜ | **Vrij aantal standby-spelers** | wij hebben er twee | L |
| ✅ | Twee onafhankelijke cartwalls met pagina's; meerdere carts tegelijk of na elkaar; faden en herhalen | Cartwall 1 en 2, Sampler | — |
| ✅ | Overlayspeler boven het programma | overlays | — |
| ✅ | Externe stream bewaken en bij signaal het programma onderbreken of erover leggen | streambewaking | — |
| ⬜ | Snelle voorluisterspeler in de verkenner (één klik, zonder de PFL-speler te bezetten) | PFL-knop per regel; een aparte speler ontbreekt | L |

### 4. Cues, golfvorm en overgangen

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Cue-editor: in, vol niveau, intro, outro, mix (volgende), fade-uit, uit; hookpunten | cue-editor | — |
| ✅ | Automatisch cues bepalen op drempels (dB voor in, volgende en uit; min/max fadelengtes) | bij het inlezen | — |
| ✅ | Mengregels per soort item en per nummer | overgangsregels per soort | — |
| ⬜ | **Maximale speelduur per soort** (bijv. jingles nooit langer dan 30 s) en **korte items niet faden** | na te bouwen in de overgangsregels | M |
| 🟡 | Gebeurtenissen op een plek in een nummer: volume-omhullende of opdracht | in de cue-editor | — |
| ⬜ | **Inzet-overgang**: een kort item (bijv. een station-ID) automatisch over het einde of het begin van een nummer leggen, met demping, tot een maximale lengte | ontbreekt | M |
| ⬜ | **Overgangseditor met twee sporen**: einde van A en begin van B onder elkaar, B verslepen, een voice-over ertussen leggen of direct inspreken, automatische demping, tempo aanpassen | **grootste ontbrekende stuk in de bediening** | H |
| ✅ | Hooks afspelen (alleen een fragment) in de planning | klokregel "Hook" | — |
| ⬜ | Hook-container met eigen opener, scheider en afsluiter (een "teaser" van komende nummers) | ontbreekt | M |
| ⬜ | Presentatortekst per item (wat je bij dit nummer of deze reclame zegt), groot in beeld tijdens het afspelen | er zijn alleen notities voor de AI-presentator | M |

### 5. Bibliotheek en metagegevens

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Mappen als bibliotheek, automatisch bijwerken, ook op shares | Bibliotheek › Beheer | — |
| ✅ | Externe database als bron (SQL) | Databronnen | — |
| ⬜ | **Een afspeellijst (M3U/PLS/XSPF) als vaste bron**, en als één doorlopend item in de log | niet in de code gevonden | M |
| ⬜ | **Centrale bibliotheekdienst** voor meerdere studio's: één plek scant en meet, de studio's vragen op en halen het bestand over als het lokaal niet bereikbaar is | stap 7 | M |
| ✅ | Snel zoeken met filters (artiest, jaar, BPM, energie, stemming, waardering, categorie) | zoektaal met chips | — |
| 🟡 | Vergelijkbare nummers vinden | via toonsoort en BPM; echte gelijkenis ontbreekt | L |
| ✅ | Uitgebreide tagvelden (componist, uitgever, ISRC, groepering, stemming, waardering) | detailpaneel | — |
| ✅ | Bibliotheek exporteren naar CSV voor een muziekplanner | /api/library/export.csv | — |
| ⬜ | Los tag-programma voor de redactie: tags, cues, volume-omhullende en inzetten vastleggen zonder de studio te openen | kan als webpagina op de engine | L |
| ⬜ | Gebruikersgebonden bibliotheken en paden (een presentator ziet alleen zijn eigen map) | rollen bestaan; padbeperking niet | L |

### 6. Automatisering en planning

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Klokken met regels: willekeurig, op volgorde, hook; scheiding op artiest/titel; niet-na-regels | planner en klokken | — |
| ✅ | Weekrooster: per dag en uur een klok | planner | — |
| ✅ | Vaste tijden: hard (onderbreken), zacht (na het huidige item), overlay | vaste gebeurtenissen | — |
| ✅ | Herinneringen, alarmen en commando's op tijd | vaste gebeurtenissen | — |
| ⬜ | **Programma's op datum** (eenmalig of met herhaling, zoals in een agenda), met eindtijd | wij plannen per weekdag en uur | H |
| ⬜ | **Maximale vertraging bij een zachte start**: is het huidige nummer bijna klaar, dan uitspelen; anders wegdraaien | zacht wacht nu altijd | M |
| ⬜ | Overlay tot een eindtijd of één ronde, daarna gaat het onderbroken programma verder | overlay speelt één item of blok | M |
| ⬜ | Handmatige start: het programma meldt zich op tijd, de presentator start het | ontbreekt | L |
| ⬜ | Overlay laten uitstellen of overslaan door de presentator vóór de start | ontbreekt | M |
| ⬜ | Mengtijd tussen twee programma's | ontbreekt | L |
| ✅ | Live-assist ↔ automatisch; de automatisering loopt mee onder de DJ | | — |
| ✅ | Containers (groep items als één regel) | klembord → container | — |
| ✅ | Globale speelgeschiedenis voor rotatie | as-run | — |
| 🟡 | Jingle of reclame automatisch na elke N–M nummers | met klokregels te maken, niet als instelling | L |
| ⬜ | Script-opties: vast eerste item, terugloopregel, archieflijst van wat gespeeld is | deels via klokken; terugloop en archief ontbreken | L |
| 🟡 | **Logs inlezen uit externe muziekplanners en traffic-systemen** | log-import met eigen veldindeling; koppelprofielen per leverancier volgen | M |

### 7. Reclame en nieuws

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Adverteerders, campagnes, blokken, uitzendbewijs per klant | Reclame | — |
| ⬜ | **Meerdere regio's**: per regio andere reclame in hetzelfde blok, op een eigen uitgang of stream, met uitzendbewijs per regio | ontbreekt | L |
| ⬜ | Reclamebeheer als losse werkplek voor de reclameafdeling, met facturatie-overzicht | kan als webpagina | M |
| ✅ | Nieuwsblokken met opener en afsluiter; RSS en berichtencentrum | | — |
| ✅ | AI-nieuws en AI-weerbericht | eigen uitbreiding | — |

### 8. Uitzenden en opnemen

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Meerdere streamservers tegelijk: Icecast, SHOUTcast v1/v2; MP3, AAC/HE-AAC, Ogg, Opus, FLAC; HLS | | — |
| ✅ | Titelupdates met eigen opmaak, per soort uit te zetten | | — |
| ⬜ | Hoes meesturen in de stream (in-stream artwork) | ontbreekt | L |
| ⬜ | Zelf als lokale streamserver dienen (luisteren op het eigen netwerk zonder Icecast) | ontbreekt | L |
| ✅ | Programma per uur opnemen met bewaartermijn; microfoon opnemen; gesprekken opnemen | | — |
| ✅ | **Opnemen met signaaldetectie** (alleen opnemen bij geluid boven een drempel, bijv. alleen de presentatorpraat als luistercontrole) | Instellingen › Opnemen › "Alleen het praten opnemen": de microfoon (als hij open staat) of een ingang (als die naar de zender gaat), drempel in dBFS en nalooptijd; een halve seconde vóór de inzet gaat mee; één MP3 per klokuur met alleen de stukken met geluid, bewaartermijn als de andere opnamen. Getest op de testengine met een testtoon: twee keer 4 en 3 s → één bestand van 8 s | H |
| ✅ | Direct inspreken naar de bibliotheek (voicetrack) | | — |

### 9. Bediening op afstand en hardware

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Commando's via HTTP, TCP, UDP en OSC; sneltoetsen die ook buiten de studio werken | commandobus | — |
| 🟡 | MIDI met leren, serieel, Livewire, Ember+, Mackie Control | gebouwd; nog niet tegen echte apparaten getest | H |
| 🟡 | **Commandotaal**: velden zoals {artiest} {titel} {tijd} zijn er; **voorwaarden, rekenen, tekstfuncties en datum/tijd-opmaak ontbreken** | EventRules en PlayoutOutputs | M |
| ⬜ | Commandoschema met **gebeurtenissen per bron** (speler, mengkanaal, stream, MIDI, OSC, serieel met patroonherkenning) die elk hun eigen velden aanbieden | gebeurtenissen per soort item zijn er | M |
| ⬜ | Commando's vanaf de opdrachtregel naar de engine sturen | klein programma op de bestaande TCP-poort | L |
| ⬜ | USB-GPIO-kaarten (contactsluitingen), D&R Airence/Webstation | stap 8 | M |
| ⬜ | GPIO als losse dienst op een andere pc (koppelt apparaten en praat met de engine via het netwerk) | onze hardwarelaag zit in de engine | L |
| ⬜ | Toegangslimiet per afstandsbediener (welke commando's mag die client) | er is één wachtwoord | M |
| ➖ | Joystickpoort | verouderd | — |

### 10. Meerdere studio's en meekijken

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Hoofd- en reservestudio met hartslag en automatische overname | reservestudio (fa3f799) | — |
| ⬜ | **Studio's elkaar laten bedienen** (log, planner en mengkanalen van een andere studio) | ontbreekt | M |
| ⬜ | **Meekijkscherm**: vorige/huidige/volgende, tijden en de stand van alle spelers, op een tweede plek of op afstand | samen met de remote-app | M |
| ⬜ | **Voicetracking op afstand** (thuis inspreken) | stap 9 | M |

### 11. Bedrijfszekerheid, logging en rapportage

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Watchdog met herstart, stiltedetectie, as-run, systeemlog | | — |
| ✅ | Programmalog als tekstbestand met eigen opmaak en bewaartermijn | | — |
| ⬜ | Logopmaak met kop, regel en slot, filter per soort item, containers wel of niet uitklappen | eigen opmaak per regel is er | L |
| ✅ | Uitzendstatistiek per dag, week en maand; rapport auteursrechten (CSV, PDF); reclamebewijs | | — |
| ⬜ | **Vrije analyse**: draaitabel en grafieken over de hele geschiedenis | uitbreiding van de statistiek | L |
| ⬜ | Log en planning afdrukken of exporteren (PDF/HTML) | alleen het muziekrapport heeft PDF | M |
| ✅ | Reservekopie en terugzetten van instellingen | | — |
| ⬜ | Eigen rapportsjablonen (opmaak zelf maken) | wij hebben vaste rapporten | L |

### 12. Gebruikersomgeving

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Dockbare vensters, meerdere schermen, werkbladen, indelingen bewaren | | — |
| ✅ | Thema's, hoge DPI, aanraakmodus, grotere letters | | — |
| ✅ | Gebruikers, rollen en rechten met pincode, studio vergrendelen | | — |
| ⬜ | **Per gebruiker**: eigen indeling, eigen beginwerkruimte, zichtbare vensters, eigen knoppen | rechten per rol; indeling per gebruiker ontbreekt | M |
| ✅ | Klok met kwartier- en uuraftelling; tijdcode (resterend tot mix, uit, intro, outro) | | — |
| 🟡 | Grote losse klok voor in de studio (apart venster of scherm), **te kiezen uit een paar soorten**: analoog met secondenstippen, digitaal, en een combinatie | venster Klok; soorten in de maak | L |
| 🟡 | **Duidelijk zien wat er speelt**: het spelende deck licht op, met een signaalbalk (niveau) en een voortgangsbalk die van kleur verandert naar het einde | deck A in Live-decks: signaalbalk (stereo, dBFS), een ON AIR-lampje dat met het geluid meegloeit en "geen geluid" na 3 s stilte; voortgang kleurt naar het einde. Nog niet: niveau per standby- en PFL-speler (de engine stuurt dat nog niet) | H |
| 🟡 | **Mooiere VU-meters**: piek en gemiddelde (RMS/LUFS) samen, piekvasthouden, schaal in dB met kleurzones, rustig vallend | eigen meter (bovenbalk, mixer, DJ-strip, deck A): LED-segmenten van -48 tot 0 dBFS, groen/oranje vanaf -9/rood vanaf -3, piek blijft 1,2 s staan, stijgt direct en valt rustig (24 dB/s) tussen de vijf updates per seconde; schaal in de mixer. Nog niet: RMS naast de piek | M |
| 🟡 | **Venster Uitsturing**: losse meters per kanaal (decks, cartwall, PFL, microfoons, inkomende lijnen, uitgangen), elk met naam en een lampje dat oplicht als het kanaal open staat | venster Uitsturing (Vensters › Geluid): programma met piek, LUFS kort/geïntegreerd, true peak en begrenzer; per mengkanaal (ook ingangen en VoIP) een grote meter met dB-schaal, piekwaarde, signaalstip en lampje OPEN/DICHT/STIL (microfoon rood MIC OPEN), bovenaan welke kanalen open zijn; meters groeien mee met het venster. Nog niet: meters per fysieke uitgang | M |
| ⬜ | **Kanaal-open-lampjes van een mengtafel**: met een MIDI- of IO-mengtafel aangesloten tonen de lampjes welke microfoon of lijn openstaat (fader op of knop aan), en "mic live" over het hele scherm | de lampjes in Uitsturing volgen nu de softwaremixer; aansluiten op de tafel wacht op de hardwarelaag (fase D) | M |
| ⬜ | Kleine bewegingen die helpen: knoppen die reageren op aanwijzen, een korte puls bij starten, een rustige knipper in de laatste tien seconden | | L |
| ⬜ | Stationsbeeld: logo, foto van de presentator, live spectrum | | L |
| ⬜ | Ingebouwd webpaneel (eigen pagina's, luisteraarsinteractie) | WebView | L |
| ⬜ | Online hulp per scherm en een handleiding | stap 10 | H |

### 13. Hulpprogramma's naast de studio

Het pakket levert naast de studio een aantal losse programma's. Bij ons horen die functies **in de engine of als webpagina van de engine**, niet als aparte installaties:

| Functie in dat pakket | Onze vorm | Stand |
|---|---|---|
| Losse tag- en cue-editor voor de redactie | webpagina op de engine | ⬜ |
| Centrale bibliotheekserver | engine in de rol "bibliotheekbron" | ⬜ |
| Reclame- en nieuwsbeheer als losse werkplek | webpagina op de engine | ⬜ |
| GPIO-dienst op een andere pc | engine in de rol "hardwarebrug" | ⬜ |
| Statistiek en analyse | werkruimte Rapportage + export | 🟡 |
| Meekijkprogramma | remote-app / meekijkscherm | ⬜ |
| Bewaker die de studio herstart | watchdog | ✅ |
| Losse studioklok | venster Klok | 🟡 |
| Commando's vanaf de opdrachtregel | `rea-cmd` | ⬜ |
| Afspeellijsten omzetten | import van open formaten | 🟡 |

### 14. Mediabeheer en workflows (tweede bron)

Het tweede platform is een systeem voor **mediabeheer en workflows voor video en tv**, geen playout. Het heeft geen log, planning of uitzendautomatisering en niets specifiek voor radio. De bruikbare ideeën zitten rond de bibliotheek: wat er gebeurt *vóórdat* iets op de lucht komt en *nadat* het gespeeld is. Video-onderdelen (montagepanelen, IMF/AS-02-pakketten, CDN- en sociale-media-koppelingen voor video, multi-tenant) laten we weg.

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Mappen bewaken en nieuwe bestanden automatisch inlezen | mappen bewaken | — |
| ✅ | Automatische controle bij binnenkomst (kwaliteit) | kwaliteitscontrole bij het inlezen | — |
| ⬜ | **Inleesstraat met eigen stappen**: per map instellen wat er met een nieuw bestand gebeurt (normaliseren of omzetten naar een standaardformaat, categorie zetten, tags aanvullen, controleren, melden), met een overzicht van lopende en mislukte taken | stappen liggen nu vast in de scanner | M |
| ⬜ | **Uploaden via een webpagina** voor mensen buiten de studio (reclamebureau, thuispresentator, verslaggever), ook grote bestanden | ontbreekt | M |
| ⬜ | **Beoordelen en goedkeuren** vóór uitzending: een reclame, voicetrack of item krijgt de status "wacht op goedkeuring"; opmerkingen op een tijdstip in de golfvorm; pas na akkoord inplanbaar | alleen voor AI-teksten ("script eerst goedkeuren") | M |
| 🟡 | Zoektaal met velden, uitsluiten en vaste woordgroepen | er is `veld:waarde`, `-woord` en `"woordgroep"`; **OF, haakjes en vergelijkingen op elk veld** ontbreken | L |
| ✅ | Bewaarde en gedeelde zoekopdrachten | slimme lijsten | — |
| ⬜ | Zoekresultaat exporteren (CSV/PDF) | alleen de hele bibliotheek als CSV | L |
| ⬜ | **Eigen velden** per soort item (bijv. "sponsor", "programma", "rechtenvrij tot") zonder code, met keuzelijsten | vaste set tagvelden | M |
| ⬜ | Trefwoordenlijsten (vaste lijst voor genre, stemming, onderwerp), zodat iedereen dezelfde termen gebruikt | vrije tekst | L |
| ⬜ | **Uitgezonden uren uitschrijven** (spraak naar tekst) en daarin zoeken: "wanneer hadden we het over de N33?" | spraakcontrole (Whisper) is er al voor de AI-presentator; uitschrijven van opnames ontbreekt | M |
| ⬜ | **Archief**: oude opnames naar goedkopere opslag (NAS of cloud) verplaatsen en op verzoek terughalen, ook een stuk op tijd | alleen een bewaartermijn (verwijderen) | L |
| ⬜ | **Publiceren na de uitzending**: een opgenomen programma als podcast of "uitzending gemist" (RSS-feed met mp3), met titel en beschrijving uit de planning | ontbreekt | M |
| ⬜ | Speler om in te sluiten op de website (live en uitzending gemist) | ontbreekt | L |
| ⬜ | Takenoverzicht: lopende en mislukte achtergrondtaken (inlezen, meten, AI-teksten, opnames, uploads) met opnieuw proberen | losse statusregels | M |
| ⬜ | Rapporten over gebruik en opslag (wie deed wat, hoeveel ruimte per map) | systeemlog en statistiek | L |
| ➖ | Aanmelden via een organisatie-account (SSO) | te zwaar voor een lokale omroep; pincodes volstaan | — |
| ➖ | Meerdere omroepen in één installatie (multi-tenant) | niet nodig | — |
| ➖ | Video: montagepanelen, videoproxy's, IMF/AS-02, video-CDN's | geen video | — |

### 15. Radio- en nieuwsproductie bij een grote omroepleverancier (derde bron)

De openbare artikelen geven vooral een beeld van **hoe een groot radiosysteem is opgedeeld**, plus één uitgebreide handleiding van een audio/video-montageprogramma in de browser. Het meeste (handleidingen, release notes van het radioproduct) staat achter een klantenlogin en is dus niet gelezen.

**Opbouw van het radiosysteem** (uit de lijst met ondersteunde geluidskaarten):

- **Op de lucht:** een uitzendspeler met carts, en daaronder een aparte uitzendserver per studio.
- **Productie:** montage met één spoor en met meerdere sporen, een trimmer en markeerprogramma, een cue-editor, een klembord, voorluisteren en een handmatige recorder.
- **Centrale opname:** geplande opnames van binnenkomende lijnen ("ingest-tijdlijn"), een **noodrecorder** (direct opnemen met één knop), en een **doorlopende opname** waaruit je achteraf een stuk knipt.
- **Hardware:** per geluidskaart staat getest wat er werkt (opnemen, productie, uitzenden, doorlopend opnemen, ASIO). Eis bij uitzenden: ten minste 4 stereo-uitgangen per uitzendserver, 48–96 kHz.

**Montage in de browser** (uit de handleiding van de editor):

- Tijdlijn met meerdere audiosporen plus een apart voice-over-spoor.
- Inspreken direct op de tijdlijn.
- **Automatisch dempen** van de andere sporen onder de voice-over, met in te stellen niveau en fades (standaard -15 dB).
- Volume-omhullende met punten.
- **Stilte aan begin en eind wegknippen** op een drempel (standaard -40 dB).
- Knippen, plakken, groeperen, rimpel- en vastklikmodus, in- en uitpunten in een trimmer.
- Bediening met J/K/L (achteruit, stop, vooruit, sneller), zelf in te stellen sneltoetsen, automatisch opslaan.

**Nieuws en verbinding:**

- Een nieuwsredactiesysteem in de cloud.
- Een netwerkgateway die de cloud en de studio veilig koppelt: VPN (IPsec, WireGuard of OpenVPN), dubbel uitgevoerd voor uitval.

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Uitzendspeler met carts; engine als aparte uitzenddienst | engine + studio | — |
| ✅ | Cue-editor, voorluisteren, klembord | | — |
| 🟡 | Handmatige recorder en voicetrack-opname | opnemen naar de bibliotheek is er; een losse recorder met trimmen ontbreekt | M |
| ⬜ | **Doorlopende opname waaruit je achteraf een stuk knipt** (bijv. een interview van 10 minuten geleden als item in de bibliotheek zetten) | uuropnames bestaan; knippen naar de bibliotheek ontbreekt | H |
| ⬜ | **Noodrecorder**: één knop die direct opneemt wat er binnenkomt (telefoon, lijn, microfoon) | ontbreekt | M |
| ⬜ | **Geplande opnames van een ingang of stream** (bijv. elke dag om 12:00 het regionale nieuws opnemen en klaarzetten) | ontbreekt | H |
| ⬜ | **Montageprogramma met meerdere sporen**: voice-over-spoor, automatisch dempen, volume-omhullende, stilte wegknippen, knippen en plakken, J/K/L | ontbreekt; hoort bij de bouwsteen tijdlijn | M |
| ⬜ | **Lijst met geteste apparatuur**: per geluidskaart en per functie (uitzenden, opnemen, ASIO) wat werkt | ontbreekt; geheugen-regel: tests alleen met apparaat 0 | M |
| ⬜ | Kleine nieuwsredactie: berichten met tekst en geluid, een draaiboek voor het bulletin, leesscherm (prompter) | nieuwsblokken bestaan; berichten en draaiboek ontbreken | M |
| ⬜ | Veilige toegang van buiten (thuisstudio, voicetracking op afstand) via een VPN zoals WireGuard, in plaats van poorten openzetten | advies en documentatie | M |
| ➖ | Cloud-gateway met BGP/OSPF, dubbel uitgevoerd | te groot voor een lokale omroep | — |
| ➖ | Centrale SQL Server-database | wij gebruiken SQLite; bij de centrale bibliotheek (fase E) opnieuw bekijken | — |

### 16. Beheer, beveiliging en kleine werkgemakken (vierde bron)

Deze artikelen gaan minder over uitzenden en meer over **hoe een omroepsysteem beheerd, beveiligd en uitgerold wordt**. Daarnaast staan er een paar handige ideeën in uit de mediabeheer-handleiding. Alles over video, Premiere-projecten, renderservers en de cloud laten we weg.

**Gebruikers en toegang**

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ✅ | Gebruikers met één rol, pincode, blokkeren na mislukte pogingen | 5 fouten → 30 s blokkade | — |
| ⬜ | Blokkeerduur en aantal pogingen instelbaar; beheerder kan een blokkade opheffen | vaste waarden | L |
| ⬜ | **Account met einddatum** (stagiair, gastpresentator, invaller) | ontbreekt | M |
| ⬜ | **Uitschakelen in plaats van verwijderen**: oude gebruikers blijven zichtbaar in as-run en statistiek | ontbreekt | M |
| ⬜ | **Activiteitenlog per gebruiker**: wie heeft wat gewijzigd (log, planning, bibliotheek, instellingen) | systeemlog zonder gebruiker per actie | M |
| ⬜ | **Geschiedenis per item**: wie veranderde welk veld wanneer | ongedaan maken bestaat, maar zonder overzicht | L |
| ⬜ | Beheerder kan "meekijken als" een gebruiker (zien wat die ziet), en dat wordt gelogd | ontbreekt | L |
| ⬜ | Meldingen per niveau naar e-mail (bijv. stilte, engine herstart, opname mislukt) | alleen in de studio | M |
| ⬜ | **Bewerkingsslot**: als iemand de log of planning van een dag open heeft, ziet een ander die alleen-lezen; de beheerder kan het slot opheffen | ontbreekt; nodig zodra meerdere plekken bewerken (fase E) | M |

**Bibliotheek en werkgemak**

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ⬜ | **Plaatshouder**: een item zonder geluid, alvast in te plannen (reclame die nog binnen moet komen); waarschuwing als het geluid er op tijd niet is | ontbreekt | M |
| ⬜ | **Markeringen met een tijdbereik** in een item (bijv. "vloek op 2:13", "goed citaat 4:00–4:20"), doorzoekbaar | cue-punten zijn er; vrije markeringen niet | L |
| ⬜ | **Stuk uit een item als nieuw item** opslaan (in- en uitpunt) | samen met het terugknippen van fase H1 | M |
| ⬜ | Kolomindelingen voor de bibliotheeklijst bewaren en delen | één vaste indeling | L |
| ⬜ | Sorteren op twee kolommen | één kolom | L |
| ⬜ | Snelle acties op een selectie (naar categorie slepen, velden in één keer zetten) | meervoudige selectie bestaat; bulk-bewerken beperkt | L |
| ⬜ | Verwijderen met keuze: bestand weg maar gegevens houden (voor statistiek en rapporten) | ontbreekt | L |
| ⬜ | Ondertitels en transcripties in- en uitvoeren als SRT/VTT (bij podcast en uitschrijven, fase G) | ontbreekt | L |
| ⬜ | Zoeken op betekenis (lokaal, zonder cloud) naast gewoon zoeken | ontbreekt | L |

**Uitrol, beveiliging en Windows** (vooral documentatie en een controlescherm)

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ⬜ | **Systeemcontrole**: één scherm dat de pc nakijkt (energiebeheer, samplefrequentie van de kaart, virusscanner op de muziekmap, vrije ruimte, klok gelijk, database in orde) | losse meldingen | M |
| ⬜ | Advies om de virusscanner de muziek-, jingle- en datamap niet live te laten scannen (geeft vertraging en mislukte toegang) | ontbreekt | M |
| ⬜ | Advies over Windows-updates: niet automatisch op de uitzend-pc; gepland en afgesproken, een week na de maandelijkse update | ontbreekt | M |
| ⬜ | **Overzicht van alle poorten** die de engine gebruikt, plus een firewallregel uit het installatieprogramma voor alleen die poorten | poort 8600 bekend, geen overzicht | M |
| ⬜ | Draaien zonder beheerdersrechten en zonder UAC uit te zetten (nagaan en vastleggen) | nog niet nagekeken | M |
| ⬜ | Advies bij haperend geluid: ASIO-buffer, stroombesparing van de processor (C-states/SpeedStep) in het BIOS | ontbreekt | L |
| ⬜ | Bij HTTPS voor toegang van buiten: volledige certificaatketen, en een controle die waarschuwt voor een verlopen of onvolledige keten | geen HTTPS | L |
| ⬜ | Databasecontrole vóór een upgrade (integriteit, schemaversie, reservekopie) met een duidelijke melding | migraties bestaan; controle vooraf ontbreekt | M |
| ⬜ | Handleiding per rol: presentator, superuser/eerste hulp, beheerder | stap 10 (handleiding) | M |
| ⬜ | **Dagelijkse automatische reservekopie van de database** met bewaartermijn (bijv. 7 dagen) op een andere schijf of NAS; muziek en jingles apart | handmatige reservekopie van instellingen | H |
| ⬜ | **Hulp op afstand die het station zelf aan- en uitzet**: een uitgaande, versleutelde verbinding (geen open poort), met een log van wie er wanneer in zat | ontbreekt; nu ad hoc | M |
| ⬜ | Meetwaarden van de engine (buffer, CPU, stilte, streamstatus) als standaard-endpoint voor een monitoringpakket, en logs 30 dagen lokaal bewaren | `/api/health` en systeemlog | L |
| ⬜ | Mailserver (SMTP) instellen voor meldingen, met een testknop | ontbreekt | M |
| ⬜ | Afsprakenlijst wie wat doet: station (pc, netwerk, reservekopieën) en beheerder (updates, storingen) | ontbreekt | L |
| ➖ | Netwerkkaart-tuning (offload uitzetten), DEP uitzetten, servicegebruikers met domeinrechten | oude Windows- en domeinadviezen; niet nodig voor onze opzet | — |
| ➖ | Koppeling met een videomontageprogramma, renderservers, offline proxy's | geen video | — |

**Les voor de architectuur:** dat systeem praat tussen machines nog deels via DCOM. Toen Microsoft DCOM strenger beveiligde, moesten alle installaties hun instellingen aanpassen, anders vielen koppelingen weg. Ze stappen nu over op gRPC. Dat bevestigt onze keuze voor **één HTTP-API voor alles**: geen afhankelijkheid van Windows-specifieke protocollen tussen machines.

### 17. Programma's, luisteraars en planning (vijfde bron)

Een helpcentrum van een playoutpakket voor kleine en middelgrote stations. Het meeste kennen we al (cue-punten, hooks, voicetracking, inlezen met controle, dubbele nummers, "wanneer laatst gespeeld", Stream Deck, verwijderen met behoud van gegevens, plaatshouders). Nieuw voor ons:

**Programma's en luisteraars**

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ⬜ | **Programma's met presentatoren en rollen**: per programma naam, korte naam, omschrijving, afbeelding en kleur; presentatoren met een rol (presentator, co-presentator, nieuwslezer, weer, verkeer, gast), één als gezicht van het programma; aan of uit voor de planning en openbaar of niet voor website en apps | stemrollen (presentator, nieuws, weer) per station; geen programma-indeling | H |
| ⬜ | **AI-profiel per programma**: doelgroep, tempo, toon en woorden die de AI-presentator vermijdt; de stem en persona volgen het programma | persona's per dagdeel | M |
| ⬜ | Een nieuw programma als concept laten invullen door het taalmodel (naam, omschrijving, AI-profiel), daarna nakijken | ontbreekt | L |
| ⬜ | Programmagegevens naar buiten: wat er nu en straks op de radio is, en een programmagids voor website en apps | "nu op de radio" via de API, zonder programma's | M |
| ⬜ | **Verzoeknummers van luisteraars**: een formulier op de eigen website (ook in te sluiten), met een keuze uit de eigen bibliotheek en een limiet per luisteraar; in de studio een wachtrij (te beoordelen, goedgekeurd, afgewezen, gepland) | wensenlijst uit Spotify; geen verzoekstroom | M |
| ⬜ | **Vervangbare plek in een klok**: een goedgekeurd verzoek of een voicetrack neemt die plek vanzelf in; zonder verzoek speelt wat de klok koos | ontbreekt | M |

**Spelen en overgangen**

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ⬜ | **Lus in een item**: een stuk van een bed herhaalt zich (met een maximum) zolang er gepraat wordt; bij verder gaan springt het naar het einde of speelt het de lus uit | lussen alleen in de DJ-modus | M |
| ⬜ | **Bed stopt vanzelf**: een item laat bij het starten lopende items met een gekozen label wegfaden (de outro haalt het bed weg) | ontbreekt | L |
| ⬜ | **Trimmen voor één overgang**: begin of einde inkorten voor alleen deze overgang, zonder het item zelf te veranderen | hoort bij de overgangseditor (B1) | M |
| ⬜ | Voorgeproduceerd programma als eigen soort item (lang, telt niet mee in de rotatie, eigen pictogram) | soorten: muziek, jingle, stem | L |
| ⬜ | Normalisatiedoel per soort item (eigen LUFS-doel voor muziek, jingles en spraak), alleen als afspeelversterking | één doel voor alles | L |

**Muziekplanning en reclame**

| | Functie | Stand bij ons | Prio |
|---|---|---|---|
| ⬜ | **Muziekkenmerken in de planning**: energie, tempo en sfeer met eigen niveaus per station, en klokregels die ze gebruiken (geen twee trage nummers na elkaar, energie opbouwen naar het uur) | zoeken op energie en stemming kan; de planner gebruikt ze niet | M |
| ⬜ | Meerdere artiesten per nummer, met een eigen weergave ("X met Y") die ook voor de artiestenscheiding telt | één artiestveld | L |
| ⬜ | **Branchescheiding in reclameblokken**: labels (bijv. auto's, supermarkt) zodat concurrenten niet in hetzelfde blok komen | dezelfde adverteerder niet twee keer in een blok | M |
| ⬜ | Speelschema van een campagne als raster per uur en weekdag, met tijdbanden en op te slaan voorkeuzes voor andere campagnes | aantallen per dag en per uur | L |
| ➖ | Licentiecodes per pc en per station, proefperiode | open bron | — |
| ➖ | Koppelingen met de muziekdiensten van die leverancier | eigen diensten van een ander | — |

### Wat REA-Radio-DJ al heeft en dat pakket niet

- AI-presentator met lokaal taalmodel en eigen stem, met een spraakcontrole.
- AI-nieuws en AI-weerbericht.
- VoIP en SIP met twee mix-minuskanalen en gespreksopname.
- DJ-modus met beatsync, automix en sampler.
- Wensenlijst uit Spotify.
- Bibliotheek met kwaliteitscontrole, herkenning van verplaatste nummers en ongedaan maken.
- Engine en studio gescheiden, met een open API (studio, web en apps praten met dezelfde engine).
- Open bron (GPL-3.0); geen licentiesleutel of dongle.

---

## Deel 2 — Benadering voor de rebuild

### Uitgangspunt

We bouwen **geen kopie**. We gebruiken de lijst hierboven als checklist en vullen de gaten met de architectuur die er al staat en bewezen werkt:

```
 studio (Avalonia)   webpagina's   remote-app   andere engines
        \                |             |            /
         └──────── HTTP-API + commandobus (TCP/UDP/OSC/MIDI) ────────┘
                                 |
                       engine (ASP.NET + BASS)
         spelers · mixer · planner · bibliotheek · stream · opname · hardware
                                 |
                        SQLite-stationsdatabase
```

Vier principes voor alles wat erbij komt:

1. **Eén engine, veel gezichten.** Nieuwe "losse programma's" worden een rol van de engine of een webpagina die de engine zelf serveert. Dan is er één installatie, één database en één plek voor rechten.
2. **Alles via de API en de commandobus.** Elke functie die je in de studio kunt bedienen, is ook een commando. Zo werken hardware, afstandsbediening, planner en gebeurtenissen vanzelf mee.
3. **Elke fase levert een werkende, geteste versie op**: unit-tests, een proef op de test-engine (apparaat 0) en screenshots. Daarna pas live, na akkoord.
4. **Eigen ontwerp en open formaten.** Geen namen, schermen of formaten van dat pakket.

### Bouwstenen die meerdere gaten tegelijk dichten

Een paar gaten hangen samen. Door eerst een gedeelde bouwsteen te maken, worden de functies erboven klein:

| Bouwsteen | Dicht | Waar |
|---|---|---|
| **Expressietaal voor commando's** (velden, voorwaarden, rekenen, tekst- en datumopmaak) | commandotaal, gebeurtenissen per bron, titelupdates, logopmaak, rapportsjablonen | Core/Remote |
| **Flexibele mixergraaf** (kanalen en bussen als lijst in de database in plaats van vast) | vrije kanalen, subgroepen, presets/profielen, talk-to-user, regio-uitgangen voor reclame | Core/Audio |
| **Agenda-laag boven het weekrooster** (programma's op datum met herhaling, start- en eindgedrag) | programma's op datum, maximale vertraging, overlay tot eindtijd, handmatige start, mengtijd | Core/Scheduling |
| **Tweesporige tijdlijn** (twee items op één tijdas met omhullende en een opnamespoor) | overgangseditor, inzet-overgang, voicetracking (lokaal en op afstand) | Core/Audio + Studio |
| **Engine-rollen** (studio, reserve, bibliotheekbron, hardwarebrug) met engine-tot-engine-API | centrale bibliotheek, studio's elkaar laten bedienen, GPIO op afstand, meekijken | Server |
| **Webpagina's op de engine** (één klein webdeel met dezelfde rechten) | tag-editor, reclamebeheer, meekijkscherm, remote-app | Server/wwwroot |

---

## Deel 3 — Plan in fases

Grootte: **S** = dagen · **M** = één tot twee weken · **L** = meerdere weken (indicatie, geen toezegging).

### Fase A — Wat er staat bedrijfszeker maken (eerst)

Zonder dit heeft uitbreiden geen zin.

| Werk | Grootte | Klaar als |
|---|---|---|
| Luistertests: DJ-modus, AI-nieuws op de zender, opnemen met bewaartermijn | S | bevindingen verwerkt, niets hoorbaar mis |
| ASIO-stuurprogramma dat niet start: terugval naar WASAPI met melding | S | engine blijft spelen, studio toont de melding |
| **Bewaking van de geluidskaart** (volume en demping van het apparaat) | S | ander programma zet de kaart zacht → melding en terugzetten |
| ✅ **Opnemen met signaaldetectie** (drempel en nalooptijd) | S | alleen de presentatorpraat staat in de opname |
| Testen tegen echte apparatuur: SIP bij de aanbieder, Ember+ tegen LUCI/Lawo, Mackie Control | M | elk apparaat werkt of heeft een bekende beperking op papier |
| Cosmetische ronde over de hele studio | M | in een smal of laag paneel is alles vindbaar |

### Fase B — Bediening en geluid op niveau

| Werk | Bouwsteen | Grootte | Klaar als |
|---|---|---|---|
| 1. **Overgangseditor met twee sporen**, met voice-over-spoor, direct inspreken en automatische demping | tijdlijn | L | een voicetrack tussen twee nummers maken en beluisteren zonder de cue-editor |
| 2. **Inzet-overgang** (station-ID over het einde van een nummer) en **maximale speelduur / niet faden per soort** | tijdlijn + overgangsregels | M | een ID valt automatisch over de outro, gedempt |
| 3. **Presentatortekst per item**, groot in beeld tijdens het afspelen | — | S | tekst bij een reclame staat in de Live-balk zolang die speelt |
| 4. **Flexibele mixergraaf**: vrij aantal kanalen, subgroepen, profielen met presets | mixergraaf | L | een extra microfoon en een groepsbus toevoegen zonder code; preset wisselen in één klik |
| 5. Hook-container met opener, scheider en afsluiter | — | S | een "straks hoor je…"-teaser uit drie hooks |
| 6. Indeling, beginwerkruimte en zichtbare vensters per gebruiker | — | M | elke presentator krijgt na aanmelden zijn eigen indeling |
| 7. **Meters en signaal**: niveau per deck en kanaal vanuit de engine, nieuwe VU-meter (piek, RMS, piekvasthouden, kleurzones), spelend deck licht op | meterstroom | M | in één oogopslag is te zien welk deck speelt en hoe hard |
| 8. **Venster Uitsturing** met een meter en een open-lampje per kanaal; met een MIDI/IO-tafel volgen de lampjes de fader of knop, "mic live" groot in beeld | meterstroom + hardwarelaag (D) | M | microfoon open op de tafel → lampje in de studio en rode balk bovenin |
| 9. Studioklok in een paar soorten (analoog, digitaal, combinatie), te kiezen per venster | — | S | elk klokvenster kan een andere soort tonen |

### Fase C — Planning en automatisering volwassen

| Werk | Bouwsteen | Grootte | Klaar als |
|---|---|---|---|
| 1. **Agenda-laag**: programma's op datum met herhaling en eindtijd boven het weekrooster | agenda | L | een eenmalige uitzending op 24 december verdringt die avond het rooster |
| 2. Startgedrag: vast, zacht met maximale vertraging, handmatig; mengtijd tussen programma's | agenda | M | zacht start nooit meer dan N seconden te laat |
| 3. Overlay tot eindtijd of één ronde; daarna verder met het onderbroken programma; uitstellen of overslaan vóór de start | agenda | M | nieuws onderbreekt en het programma loopt verder waar het was |
| 4. **Afspeellijst als vaste bron** en als één doorlopend item | — | S | een M3U-map speelt als categorie |
| 5. Koppelprofielen voor externe muziekplanners en traffic (eerst het formaat dat het station gebruikt) | — | M | een geïmporteerde dag speelt zonder handwerk |
| 6. Log en planning afdrukken of exporteren (PDF/HTML) | — | S | het draaiboek van morgen ligt op papier |

### Fase D — Commando's en hardware

| Werk | Bouwsteen | Grootte | Klaar als |
|---|---|---|---|
| 1. **Expressietaal** met velden, voorwaarden en functies in alle gebeurtenissen en titelupdates | expressietaal | M | "als soort = jingle dan geen titelupdate" zonder code |
| 2. Gebeurtenissen per bron: speler, mengkanaal, stream, MIDI, OSC, serieel met patroonherkenning | expressietaal | M | een fader-start op de tafel start de juiste speler |
| 3. Toegangslimiet per afstandsbediener | — | S | de tablet mag geen instellingen wijzigen |
| 4. `rea-cmd` op de opdrachtregel | — | S | een ander programma stuurt "NEXT" |
| 5. USB-GPIO en D&R Airence/Webstation via de algemene hardwarelaag (alleen met het openbare protocol) | — | M | knoppen en lampjes werken op de echte tafel |

### Fase E — Meerdere plekken

| Werk | Bouwsteen | Grootte | Klaar als |
|---|---|---|---|
| 1. **Centrale bibliotheekdienst** (engine-rol "bibliotheekbron"; bestand overhalen als het lokaal niet bereikbaar is) | engine-rollen | L | studio 2 zoekt en speelt uit de bibliotheek van studio 1 |
| 2. Studio's elkaar laten bedienen (log, planner, mengkanalen) | engine-rollen | M | vanuit studio 2 de log van studio 1 aanpassen |
| 3. **Meekijkscherm en remote-app** (tablet bedienen, telefoon bewaken) | webpagina's | M | op de telefoon zie je wat er speelt en wat er komt |
| 4. **Voicetracking op afstand** (thuis inspreken in de overgangseditor via de webpagina) | tijdlijn + webpagina's | L | een presentator spreekt thuis een uur in |
| 5. Hardwarebrug op een andere pc | engine-rollen | S | GPIO zit aan een pc in de technische ruimte |

### Fase F — Redactie, reclame en afwerking

| Werk | Grootte | Klaar als |
|---|---|---|
| Webpagina's voor de redactie: tags en cues zonder studio; reclamebeheer met facturatie-overzicht | M | de reclameafdeling werkt zonder de studio |
| Reclame per regio (eigen uitgang of stream per regio, bewijs per regio) | M | alleen als het station het nodig heeft |
| BWF/CART lezen en schrijven, RF64 | S | cue-gegevens uit omroepbestanden komen mee |
| Vrije statistiekanalyse (draaitabel, grafieken) en eigen rapportsjablonen | M | eigen overzicht zonder Excel |
| In-stream artwork, lokale streamserver, stationsbeeld, webpaneel, grote studioklok | M | — |
| **Handleiding** voor de studio en de beheerder, met hulp per scherm | M | een nieuwe presentator kan zelf beginnen |

### Fase G — Vóór en na de uitzending (uit onderdeel 14)

| Werk | Bouwsteen | Grootte | Klaar als |
|---|---|---|---|
| 1. **Takenoverzicht** voor alle achtergrondtaken, met opnieuw proberen | — | S | een mislukte meting of upload is zichtbaar en opnieuw te starten |
| 2. **Inleesstraat met stappen per map** (omzetten, categorie, tags, controle, melden) | expressietaal | M | een nieuwe reclame in de map "Reclame" staat genormaliseerd en gecontroleerd klaar |
| 3. **Uploaden via een webpagina**, met de status "wacht op goedkeuring" | webpagina's | M | een reclamebureau levert een spot aan zonder de studio |
| 4. **Beoordelen en goedkeuren** met opmerkingen op de golfvorm | webpagina's | M | een voicetrack wordt pas inplanbaar na akkoord |
| 5. **Podcast / uitzending gemist**: opname van een programma als RSS-feed, titel uit de planning | — | M | het programma van gisteren staat vanzelf in de feed |
| 6. **Opnames uitschrijven** (lokaal, met de Whisper-dienst die er al is) en doorzoeken | — | M | zoeken op een woord vindt het uur en de minuut |
| 7. Eigen velden en trefwoordenlijsten | — | M | het veld "sponsor" staat in het detailpaneel en in de zoektaal |
| 8. Zoektaal: OF, haakjes, vergelijkingen; resultaat exporteren | — | S | `genre:(pop OF rock) jaar:>2000` werkt |
| 9. Archief naar NAS of cloud met terughalen | engine-rollen | L | opnames ouder dan een maand staan op de NAS en zijn terug te halen |

### Fase H — Opnemen en produceren (uit onderdeel 15)

| Werk | Bouwsteen | Grootte | Klaar als |
|---|---|---|---|
| 1. **Doorlopende opname met terugknippen**: in de opname van de afgelopen uren een stuk aanwijzen (golfvorm, tijd) en als item in de bibliotheek zetten | tijdlijn | M | het interview van 10 minuten geleden staat binnen een minuut als item klaar |
| 2. **Geplande opnames** van een ingang, VoIP-lijn of stream, met een categorie als bestemming | agenda | M | het regionale nieuws van 12:00 staat elke dag vanzelf klaar |
| 3. **Noodrecorder**: één knop (ook als commando en op de hardware) die direct opneemt wat er binnenkomt | — | S | een onverwacht telefoontje staat volledig in de opname |
| 4. **Montageprogramma met meerdere sporen**: voice-over-spoor met inspreken, automatisch dempen, omhullende, stilte wegknippen, knippen/plakken, J/K/L | tijdlijn | L | een nieuwsitem met intro, interview en muziekbed maken zonder ander programma |
| 5. **Lijst met geteste apparatuur** in de documentatie, bijgehouden bij elke test | — | S | de beheerder weet vooraf welke kaart werkt |
| 6. Kleine nieuwsredactie: berichten met tekst en geluid, draaiboek voor het bulletin, leesscherm | webpagina's | M | de nieuwslezer leest van het scherm en start de geluidsfragmenten vanuit het draaiboek |
| 7. Handleiding voor veilige toegang van buiten via WireGuard | — | S | voicetracking op afstand werkt zonder open poorten |

### Fase I — Beheer en beveiliging (uit onderdeel 16)

| Werk | Bouwsteen | Grootte | Klaar als |
|---|---|---|---|
| 1. **Systeemcontrole-scherm** en een beheerdershandleiding (virusscanner, Windows-updates, poorten, ASIO/BIOS) | — | M | een nieuwe uitzend-pc is in een half uur nagekeken |
| 2. Firewallregel in het installatieprogramma; draaien zonder beheerdersrechten nagaan | — | S | de engine is van buiten alleen bereikbaar op de poorten die nodig zijn |
| 3. **Activiteitenlog per gebruiker** en geschiedenis per item | — | M | "wie heeft de log van vrijdag veranderd?" is te beantwoorden |
| 4. Account met einddatum, uitschakelen in plaats van verwijderen, instelbare blokkade | — | S | een stagiair kan na zijn laatste dag niet meer inloggen |
| 5. Meldingen per niveau naar e-mail | — | S | stilte 's nachts komt als mail binnen |
| 6. **Plaatshouder** in de planning, met waarschuwing als het geluid ontbreekt | — | S | een ingeplande reclame zonder bestand geeft een dag vooraf een melding |
| 7. **Bewerkingsslot** op log en planning | engine-rollen | M | twee plekken kunnen niet tegelijk dezelfde dag overschrijven |
| 8. Databasecontrole vóór een upgrade | — | S | een upgrade begint niet zonder geslaagde controle en reservekopie |
| 9. **Dagelijkse reservekopie van de database** met bewaartermijn en een terugzet-test | — | S | na een kapotte schijf staat de bibliotheek van gisteren binnen een uur terug |
| 10. Hulp op afstand via een uitgaande verbinding die het station aan- en uitzet, met toegangslog | — | M | de beheerder kan helpen zonder open poort; het station ziet wie er was |

### Fase J — Programma's, luisteraars en planning (uit onderdeel 17)

| Werk | Bouwsteen | Grootte | Klaar als |
|---|---|---|---|
| 1. **Programma's** met presentatoren, rollen en een AI-profiel; stem en persona volgen het programma | agenda | M | in "De Zondagmiddag" spreekt het weer met de stem van de vaste weervrouw |
| 2. Programmagids en "nu op de radio" naar buiten (API en webpagina) | webpagina's | S | de website toont het programma van nu en straks |
| 3. **Verzoeknummers** via een webformulier, met wachtrij in de studio en een vervangbare klokplek | webpagina's | M | een verzoek van de website staat na goedkeuring vanzelf in het volgende uur |
| 4. **Lus in een item** en bed dat vanzelf stopt | — | S | een bed loopt door tot de presentator klaar is en verdwijnt als de outro start |
| 5. **Muziekkenmerken in de klokregels** (energie, tempo, sfeer) | expressietaal | M | een uur bouwt hoorbaar op, zonder twee trage nummers na elkaar |
| 6. **Branchescheiding** in reclameblokken en een speelschema als raster | — | S | twee autobedrijven staan nooit in hetzelfde blok |
| 7. Normalisatiedoel per soort item, voorgeproduceerd programma als soort, meerdere artiesten | — | S | spraak klinkt even hard als muziek zonder elk item na te lopen |

### Volgorde en afhankelijkheden

```
A ──► B ──► C ──► E
      │     │
      └──► D┘     F, G en H (lopen parallel zodra A klaar is)
```

- **H1 en H4** gebruiken dezelfde tijdlijn als de overgangseditor (**B1**). Bouw de tijdlijn één keer, met plek voor meerdere sporen.
- **H2** past in de agenda-laag van **C1**.
- **I7 (bewerkingsslot)** moet er zijn vóór **E2** (studio's elkaar laten bedienen). **I1–I2** passen goed bij **A** (bedrijfszeker).
- **J1 (programma's)** bouwt voort op de agenda-laag van **C1** en op de stemrollen; **J3** gebruikt dezelfde webbasis als **E3** en **G**. **J5** gaat sneller met de expressietaal (**D1**).

- **G** hangt voor de webpagina's (3, 4) samen met **E3**: dezelfde webbasis. G1 (takenoverzicht) is klein en helpt meteen bij de bedrijfszekerheid uit A.

- **A** eerst: zonder bedrijfszekerheid geen uitbreiding.
- **B1 (tijdlijn)** is nodig voor **E4** (voicetracking op afstand).
- **D1 (expressietaal)** maakt **C** en de logopmaak in **F** kleiner; kan ook direct na A.
- **F** kan per onderdeel tussendoor, als het station erom vraagt.

---

## Bewust niet

| Wat | Waarom |
|---|---|
| Windows Media-streaming en -server, joystickpoort | verouderd |
| Cd's rippen en branden | staat al onder "Wat niet op de lijst staat" in de werklijst |
| Registratie aan pc, harde schijf of USB-stick | REA-Radio-DJ is open bron |
| Eigen afspeellijst-, script- of databaseformaat van dat pakket, en zijn commandonamen | clean-room: alleen open formaten en eigen namen |
| Inlezen van eigen formaten van andere commerciële playouts | alleen als het formaat openbaar beschreven is |
