# Werklijst naar een werkende applicatie

Deze lijst zet alles wat op 9 en 10 november 2026 is gevraagd en getoond in een vaste volgorde. De volledige
inventaris met status per onderdeel staat in [ROADMAP.md](ROADMAP.md); hier staat **in welke volgorde** het gebouwd
wordt, **wanneer een stap af is** en **welke vragen** er per stap nog openstaan.

Uitgangspunten:

- Alles is eigen ontwerp. Schermafbeeldingen van andere pakketten zijn gebruikt als controlelijst van functies; namen,
  schermen, teksten en bestandsformaten worden niet overgenomen.
- Alleen open protocollen en codecs. Geen eigen codec of licentiemechanisme van een ander product.
- Elke stap levert iets op dat werkt en getest is; pas dan volgt de volgende.
- Waar een vraag openstaat en er een verstandige standaard is, wordt met die standaard gebouwd. De vraag blijft staan
  en het antwoord past later de instelling aan, niet het ontwerp.

Legenda: ✅ klaar · 🟡 deels · ⬜ open

## Waar we staan

| Onderdeel | Stand |
|---|---|
| Automatisering, planner, log, spelers, cartwalls, mixer, stream, AI-presentator, voicetracking, nieuws, reclame, rapportage | ✅ (zie ROADMAP, blok Klaar en rondes 2 en 3) |
| Hardware: MIDI, serieel, Livewire, Ember+ (op nummer of naam) | ✅, Ember+ nog niet tegen een echt apparaat getest |
| Nieuwe bovenbalken (app-balk en aan-de-luchtbalk) | ✅ |
| DJ-modus: vier decks, mengpaneel, werkruimte, overnemen van de automatisering | 🟡 werkt, nog niet beluisterd |
| Venster Instellingen | ✅ zeven tabbladen |
| Verbindingen (codec, VoIP) | ⬜ alleen ontwerp |

## Beantwoorde vragen (10 november 2026)

| Vraag | Antwoord | Gevolg voor de bouw |
|---|---|---|
| 2. Waar draait de engine? | Op dezelfde pc als de studio | Engine start bij aanmelden met een watchdog naast zich; geen aparte dienst-pc nodig. Een Windows-dienst blijft mogelijk voor later |
| 3/4. Geluidskaarten | Alles moet instelbaar zijn in het configuratiescherm: Windows-geluid en ASIO | Per uitgang kiezen tussen Windows (WASAPI/DirectSound) en ASIO, met buffer, samplerate en testtoon |
| 6. Schermen | 3 tot 4 schermen, er kan een aanraakscherm bij | Indelingen voor 1 tot 4 schermen; aanraakmodus met grote knoppen per scherm instelbaar |
| 8. DJ-controller | Hercules en Pioneer | Standaardtoewijzingen voor een Hercules DJControl- en een Pioneer DDJ-controller (MIDI), aan te passen in Instellingen |
| 10/11. VoIP | Een SIP-account kan; LUCI Live moet ook kunnen | SIP-registratie bij een aanbieder; LUCI Live verbindt op een open codec (Opus/G.722) via SIP of RTP |

## Beantwoorde vragen (10 november 2026, tweede ronde)

| Vraag | Antwoord | Gevolg voor de bouw |
|---|---|---|
| 1. Stilte | Een aparte reserve-afspeellijst, of vrij willekeurig uit de muziekbibliotheek | Naast "noodcategorie" ook de actie "willekeurig uit de hele muziekbibliotheek"; is de noodcategorie leeg, dan valt hij daarop terug in plaats van op de automatisering |
| 3. Uitgangen naar de tafel | 4 tot 6, waarvan 2 voor VoIP | Uitgangen per kanaal blijven zoals ze zijn; VoIP krijgt twee eigen mengkanalen met elk een eigen uitgang |
| 4. Software of tafel | Beide moeten kunnen: normaal mengt de tafel live, bij automatisering mengt de software | Twee werkwijzen: "mengtafel" (elk kanaal op vol naar een eigen uitgang) en "software" (de engine mengt naar de zender-uitgang); omschakelen zonder herstart |
| 5. VST | Ja, nodig voor de proof of concept | VST-plekken per kanaal horen bij stap 2 |
| 7. Thema | Ja, meerdere thema's, ook licht, met afgeronde vormen, goede bedienbaarheid en kleuren | Stap 3: themakeuze (donker, licht en meer), afronding en kleuren instelbaar |
| 8. DJ-controller | Ook oudere typen van Hercules en Pioneer | Algemene MIDI-toewijzing met "leren" (knop aanraken = koppelen) en voorinstellingen per controller, niet vast op één type |
| 9. Automatisering onder de DJ | Ja, onhoorbaar doorlopen, en duidelijk tonen met een oplichtende knop of veld | De automatisering loopt stil mee terwijl de DJ op de lucht is; een opvallend veld "automatisering loopt mee" met één knop om terug te geven |
| 10. SIP-aanbieder | Een PortaOne-centrale, nu voip.sipnl.net; moet flexibel instelbaar zijn | SIP-account met alle gangbare velden: registrar, proxy, outbound proxy, domein, gebruikers- en authenticatienaam, transport (UDP/TCP/TLS), poort, STUN, codecvolgorde, DTMF-methode, herregistratietijd |
| 12. Bereikbaarheid | Externe telefonie loopt via de SIP-server van voip.sipnl.net | Geen open poorten nodig: registreren en bellen via de aanbieder, met NAT-ondersteuning (STUN, keep-alive, rport) |
| 13. Streams | Alle bekende formaten van gangbare stream-encoders | Icecast en SHOUTcast (v1 en v2) met MP3, AAC/HE-AAC, Ogg Vorbis, Opus en FLAC; meerdere servers tegelijk; HLS |
| 14. Bewaren | Instelbaar, normaal een jaar | Bewaartermijn van opnames instelbaar, standaard 365 dagen |
| 15. Rapportsjabloon | Geen voorbeeld; zelf iets maken. Voor reclame ook een los afspeelrapport | Eigen rapportindeling (CSV en PDF); apart reclame-afspeelrapport per adverteerder en campagne als bewijs voor klanten en stichtingen, ook als export (CSV/JSON) om later aan een ander systeem te koppelen |
| 16. Hardware | D&R Airence en D&R Webstation; het moet voor meerdere fabrikanten werken | Eerst D&R Airence en Webstation, via een algemene laag voor tafels zodat andere fabrikanten erbij kunnen |

## Proof of concept

De proof of concept is af na **stap 1 tot en met 5**: een station dat niet stilvalt, geluid dat naar de juiste kaarten
gaat, één herkenbare stijl, een DJ-tafel waarop je echt kunt mixen en één werkende live-verbinding. Stap 6 tot en met 10
maken er een volledig product van.

## Stap 1 — Bedrijfszeker

Eerst zorgen dat de zender niet stil kan vallen; alles daarna bouwt daarop.

| Status | Werk | Af als |
|---|---|---|
| ✅ | Stilte: ook "willekeurig uit de hele muziekbibliotheek" als actie en als terugval bij een lege noodcategorie (vraag 1). Getest: na 5 s stilte speelt een willekeurig nummer, ook bij een noodcategorie die niet bestaat | stilte speelt direct een willekeurig nummer, ook zonder noodcategorie |
| ✅ | Stiltedetectie op de zender: drempel, duur en actie (melden, de automatisering starten, of direct iets uit een noodcategorie spelen), in te stellen bij Instellingen › Bewaking | stilte van de ingestelde duur geeft een alarm en de gekozen actie; getest met een stille engine |
| ✅ | Watchdog: de engine schrijft een hartslag; een klein bewakingsproces start hem opnieuw na een crash of vastloper, en hij gaat verder waar de log was | engine geforceerd stoppen → binnen 15 s weer op de lucht met dezelfde log. Getest: na 1,4 s terug, hetzelfde nummer op dezelfde plek. Een vastloper (engine reageert niet meer) is gebouwd maar niet nagebootst |
| 🟡 | Engine bij aanmelden starten op de studio-pc, los van de studio (dienst later): Instellingen › Systeem | na herstart van de pc speelt het station zonder dat iemand iets doet. Aan- en uitzetten getest; een echte herstart van de pc nog niet |
| ✅ | Reservekopie van instellingen, klokken, planning, cartwalls en hardware in één bestand; terugzetten | kopie terugzetten op een lege datamap geeft hetzelfde station |
| ✅ | Systeemlog van de engine in de studio, met niveau gewoon of uitgebreid | fouten zijn in de studio terug te lezen zonder in bestanden te zoeken |

Vragen bij stap 1:

1. ~~Wat moet er bij stilte gebeuren?~~ **Reserve-afspeellijst of willekeurig uit de muziekbibliotheek.**
2. ~~Draait de engine straks op dezelfde pc als de studio?~~ **Ja, dezelfde pc.**

## Stap 2 — Geluid en mengpaneel

| Status | Werk | Af als |
|---|---|---|
| ✅ | Uitgang per mengkanaal: automatisering, standby, cartwalls, overlays, DJ, microfoon en voorluisteren elk naar een eigen Windows-kaart of kanaalpaar (1/2 t/m 7/8), Instellingen › Geluid. VoIP volgt met stap 5 | elk kanaal is in Instellingen op een eigen uitgang te zetten en blijft daar na een herstart. Getest op het apparaat "geen geluid"; een kanaalpaar 3/4 op een echte meerkanaalskaart nog niet |
| ✅ | Ingangen als mengkanaal: meer dan één microfoon, lijn-in (cd-speler, tweede studio), met doorzetknop — Mixer › Ingangen: elk invoerapparaat of een testtoon als kanaal, start dicht, ZENDER zet hem door; ook een eigen uitgang mogelijk. Getest met de testtoon; een echte lijn-in nog niet | een lijn-ingang staat als kanaal in de mixer en kan op de zender |
| 🟡 | ASIO-uitvoer en instelbare buffer en samplerate, met testtoon per uitgang: klaar in Instellingen › Geluid (uitzending, voorluisteren en elk mengkanaal via ASIO; 44,1 of 48 kHz; buffer). Een stuurprogramma dat niet start wordt geweigerd en de Windows-kaart blijft in gebruik. Getest met Realtek ASIO; nog niet beluisterd en niet met een studiokaart | ASIO-kaart speelt zonder haperen; testtoon klinkt op de gekozen uitgang |
| ✅ | Kanaalstrip per mengkanaal: gain, balans, equalizer, compressor, automatische niveauregeling, dempen, naar de zender, opnemen — klaar via ⚙ op elk kanaal in de Mixer (gemeten op de zender: gain +6 dB, balans, midden -12 dB, compressor, niveauregeling; bewaard na herstart). Opnemen: REC-knop op elk kanaal, WAV in de map Opnamen van de engine, na de kanaalstrip en voor de fader (getest: testtoon met fader dicht en gedempt staat er precies op -18 dBFS / 1 kHz in) | instellingen per kanaal hoorbaar en bewaard |
| ✅ | Master: begrenzer en loudness-meter op de zender | pieken boven de grens worden begrensd; LUFS zichtbaar — eigen look-ahead-begrenzer (plafond instelbaar, Geluid-tab), LUFS kort/geïntegreerd en true peak bij de meter; getest: plafond -12 dB → true peak -11,5 (zonder -7) |
| ⬜ | Plekken voor VST-effecten per kanaal (nodig voor de proof of concept, vraag 5) | één VST-plug-in laadt en werkt op een kanaal |
| ⬜ | Werkwijze mengtafel of software: bij "mengtafel" gaat elk kanaal op vol naar zijn eigen uitgang, bij "software" mengt de engine (vraag 4) | omschakelen werkt zonder herstart en blijft bewaard |
| ✅ | DSP-effect op een fader: per mengkanaal een ingebouwd effect kiezen (bijv. galm, echo, filter, stemvervorming), met aan/uit en mengverhouding, vanuit de kanaalstrip in de Mixer (verzoek studio, 10-11-2026) — galm, echo, chorus, flanger, telefoon, stem lager/hoger; FX-knop op de fader. Gemeten: echo 0,126 → 0,24, telefoon dempt 100 Hz en 8 kHz sterk, stem hoger maakt van 1 kHz 1260 Hz | een gekozen effect is hoorbaar op dat kanaal, is aan en uit te zetten en blijft bewaard na een herstart |

Vragen bij stap 2:

3. ~~Welke geluidskaart(en)?~~ **Instelbaar: Windows-geluid en ASIO; 4 tot 6 uitgangen naar de tafel, waarvan 2 voor VoIP.**
4. ~~Software of mengtafel?~~ **Beide: live mengt de tafel (D&R Airence of Webstation), bij automatisering de software.**
5. ~~VST nodig?~~ **Ja, voor de proof of concept.**

## Stap 3 — Eén stijl in de hele studio

| Status | Werk | Af als |
|---|---|---|
| ⬜ | Spelers en cartwall in de nieuwe stijl: toestand als woord en vorm, cartknoppen van 96 px | gelijk aan het ontwerp "Spelers" |
| ⬜ | Programmalog in de nieuwe stijl | gelijk aan het ontwerp "1 scherm" |
| ⬜ | Werkruimtes Productie, Planning en Rapportage naast Live, DJ en Automatisering | elke werkruimte opent zijn eigen indeling |
| ⬜ | Responsive gedrag en indelingen voor 1 tot 4 schermen, aanraakmodus voor een aanraakscherm | op 1 tot 4 schermen staat alles op zijn plek en blijft dat na een herstart |
| ⬜ | Overige vensters nalopen (planner, bibliotheek, mixer, beheer) | geen venster meer in de oude stijl |
| ⬜ | Uiterlijk instelbaar: meerdere thema's (donker, licht en meer), afgeronde vormen, lettergrootte, aanraakmodus, kleuren per soort item (vraag 7) | in Instellingen te kiezen, direct zichtbaar |
| ⬜ | Eigen knoppen, regiekamer-venster (weer, nieuwskoppen, berichten), itemgegevens, verkenner | vensters aanwezig en te docken |

Vragen bij stap 3:

6. ~~Hoeveel schermen?~~ **3 tot 4, met eventueel een aanraakscherm.**
7. ~~Licht thema?~~ **Ja, meerdere thema's met afgeronde vormen en goede bedienbaarheid.**

## Stap 4 — DJ-modus afmaken

| Status | Werk | Af als |
|---|---|---|
| ⬜ | Luistertest van wat er staat: tempo, sync, loops, overnemen; bijstellen van vertraging en overgang | overnemen is onhoorbaar; een loop van 4 beats loopt strak |
| ⬜ | Twee doorlopende golfvormen boven elkaar met beatstrepen | beats van twee decks zijn op het oog gelijk te leggen |
| ⬜ | Toonsoort meten; BPM, toonsoort en ReplayGain al bij het inlezen, zichtbaar in de bibliotheek | bibliotheek sorteert op BPM en toonsoort |
| ⬜ | Quantize, loop-roll, cue-mix op de koptelefoon, begrenzer op de DJ-uitgang | — |
| ⬜ | Effecten per deck (echo, flanger, reverb) op de beat | — |
| ⬜ | Sampler: cartwall als pads in de DJ-werkruimte | — |
| ⬜ | Automix in de DJ-modus met overgangstypes; tempo-overgang ook in de automatisering | een lijst van tien nummers mixt zichzelf |
| ⬜ | Automatisering loopt onhoorbaar door terwijl de DJ op de lucht is, met een oplichtend veld en één knop om terug te geven (vraag 9) | DJ van de lucht → automatisering is meteen hoorbaar op de juiste plek |
| ⬜ | Bediening van decks en mengpaneel via de commandobus: MIDI leren en voorinstellingen voor Hercules en Pioneer, ook oudere typen (vraag 8) | een controller bedient start, cue, tempo, EQ en crossfader |
| ⬜ | Mix opnemen | opname staat als bestand in de bibliotheek |
| ⬜ | Draaiknoppen in plaats van schuiven, en het geheel passend op 1920 × 1080 zonder scrollen | — |
| ⬜ | Stems (zang, instrumenten, bas, drums) | later; grootste onderdeel, niet nodig voor de proof of concept |

Vragen bij stap 4:

8. ~~Welke dj-controller?~~ **Hercules en Pioneer, ook oudere typen: dus leren en voorinstellingen.**
9. ~~Automatisering laten doorlopen?~~ **Ja, onhoorbaar, met een oplichtend veld.**

## Stap 5 — Verbindingen (codec, VoIP)

| Status | Werk | Af als |
|---|---|---|
| ⬜ | Mengkanaal VoIP met eigen fader, voorluisteren en terugkanaal zonder zichzelf | — |
| ⬜ | RTP in twee richtingen met Opus, G.722, G.711 en PCM; jitterbuffer met meting | twee engines praten met elkaar over het netwerk |
| ⬜ | SIP volgens EBU N/ACIP: bellen, gebeld worden, registreren bij een PortaOne-centrale (nu voip.sipnl.net), alle accountvelden instelbaar, met NAT-ondersteuning (vraag 10 en 12) | registreren bij de aanbieder en een telefoongesprek op de zender |
| ⬜ | Twee VoIP-mengkanalen, elk met een eigen uitgang naar de tafel (vraag 3) | — |
| ⬜ | Venster Verbindingen volgens het ontwerp, met stations en presets | — |
| ⬜ | Bediening en status op de commandobus (knoppen, lampjes, Ember+) | — |
| ⬜ | Later: SRT, twee netwerken tegelijk, Icecast als bron, NDI- of ASIO-ingang, tekstberichten, opname | — |

Vragen bij stap 5:

10. ~~Is er een SIP-account?~~ **Ja: PortaOne-centrale, nu voip.sipnl.net; flexibel instelbaar.**
11. ~~Waarmee belt een verslaggever in?~~ **Ook LUCI Live** (op een open codec).
12. ~~Bereikbaarheid?~~ **Via de SIP-server van voip.sipnl.net.**

## Stap 6 — Opnemen en uitzenden

| Status | Werk |
|---|---|
| ⬜ | Opnemen van programma en microfoon; automatisch per uur met bewaartermijn (instelbaar, standaard een jaar); direct opnemen naar de bibliotheek |
| ⬜ | Meerdere streamservers tegelijk: Icecast en SHOUTcast v1/v2; MP3, AAC/HE-AAC, Ogg Vorbis, Opus en FLAC; HLS |
| ⬜ | Titelupdates: eigen opmaak, soorten uitsluiten, ook naar een bestand of webadres |
| ⬜ | Programmalog als tekstbestand met eigen opmaak en bewaartermijn |

Vragen bij stap 6:

13. ~~Streamservers?~~ **Alle gangbare servers en formaten.**
14. ~~Bewaren?~~ **Instelbaar, standaard een jaar.**

## Stap 7 — Bibliotheek en planning

| Status | Werk |
|---|---|
| ⬜ | Meer dan twee bibliotheekmappen, automatisch opnieuw inlezen, vervangende locaties |
| ⬜ | Tags lezen en terugschrijven, hoezen |
| ⬜ | Overgangen en mixpunten per soort item en per nummer |
| ⬜ | Centrale bibliotheek voor meerdere studio's |
| 🟡 | Rapportage auteursrechten in een eigen indeling (CSV en PDF); er is geen voorgeschreven sjabloon |
| ⬜ | Reclame-afspeelrapport per adverteerder en campagne, als bewijs voor klanten en stichtingen; export als CSV en JSON om later te koppelen |

Vraag bij stap 7:

15. ~~Sjabloon?~~ **Geen; eigen indeling, plus een los reclame-afspeelrapport.**

## Stap 8 — Bediening en hardware

| Status | Werk |
|---|---|
| ⬜ | Gebeurtenissen koppelen aan commando's (start van een item, microfoon open, stream weg) |
| ⬜ | Bediening via een webadres per knop en een UDP-poort; sneltoetsen die ook buiten de studio werken |
| ⬜ | OSC uitsturen |
| ⬜ | USB-GPIO-kaarten, joystickpoort, tweede Ember+- en Livewire-apparaat als reserve |
| ⬜ | Mengtafels: eerst D&R Airence en D&R Webstation, via een algemene laag zodat AxelTech, Wheatstone en andere fabrikanten erbij kunnen; elk met het openbare protocol van de fabrikant |
| 🟡 | Ember+ testen tegen LUCI Studio en een Lawo-tafel |

Vraag bij stap 8:

16. ~~Welke apparaten?~~ **D&R Airence en D&R Webstation; uitbreidbaar naar andere fabrikanten.**

## Stap 9 — Gebruikers en meerdere studio's

| Status | Werk |
|---|---|
| ⬜ | Gebruikers, rollen en rechten; studio vergrendelen; instellingen alleen voor beheerders |
| ⬜ | Multi-studio met hartslag en automatische overname |
| ⬜ | Voicetracking op afstand |
| ⬜ | Remote-app voor tablet (bedienen) en telefoon (bewaken) |

## Stap 10 — Afwerking

| Status | Werk |
|---|---|
| ⬜ | Inrichthulp bij de eerste start |
| ⬜ | Installatieprogramma, controle op nieuwe versies, pagina Over |
| ⬜ | Handleiding voor de studio en voor de beheerder |

## Wat niet op de lijst staat

- Cd's rippen en branden.
- Inloggen op of doorzetten van de muziekcatalogus van een dj-pakket.
- De eigen codec van LUCI (ULCC).
