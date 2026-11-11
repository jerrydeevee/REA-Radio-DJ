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
| ✅ | Plekken voor VST-effecten per kanaal (nodig voor de proof of concept, vraag 5) — drie plekken per kanaal in de kanaalstrip (na het effect, voor de compressor), plug-ins zoeken in instelbare mappen, aan/uit en parameters als schuiven, bewaard na herstart. Via bass_vst: alleen VST 2 (64-bit), geen VST 3. Fouten (geen VST, bestand weg) staan bij de plek en de engine speelt door. Getest met Airwindows PurestGain en EQ (open source, MIT): gain op -12,8 dB gaf precies -12,8 dB minder op het kanaal, uitzetten gaf het oude niveau terug, en na herstart stonden plug-ins en waarden er weer | één VST-plug-in laadt en werkt op een kanaal |
| 🟡 | Werkwijze mengtafel of software: bij "mengtafel" gaat elk kanaal op vol naar zijn eigen uitgang, bij "software" mengt de engine (vraag 4) | omschakelen werkt zonder herstart en blijft bewaard (Instellingen → Uitgangen → Werkwijze; getest op apparaat 0, nog niet met een echte tafel) |
| ✅ | DSP-effect op een fader: per mengkanaal een ingebouwd effect kiezen (bijv. galm, echo, filter, stemvervorming), met aan/uit en mengverhouding, vanuit de kanaalstrip in de Mixer (verzoek studio, 10-11-2026) — galm, echo, chorus, flanger, telefoon, stem lager/hoger; FX-knop op de fader. Gemeten: echo 0,126 → 0,24, telefoon dempt 100 Hz en 8 kHz sterk, stem hoger maakt van 1 kHz 1260 Hz | een gekozen effect is hoorbaar op dat kanaal, is aan en uit te zetten en blijft bewaard na een herstart |

Vragen bij stap 2:

3. ~~Welke geluidskaart(en)?~~ **Instelbaar: Windows-geluid en ASIO; 4 tot 6 uitgangen naar de tafel, waarvan 2 voor VoIP.**
4. ~~Software of mengtafel?~~ **Beide: live mengt de tafel (D&R Airence of Webstation), bij automatisering de software.**
5. ~~VST nodig?~~ **Ja, voor de proof of concept.**

## Stap 3 — Eén stijl in de hele studio

| Status | Werk | Af als |
|---|---|---|
| ✅ | Spelers en cartwall in de nieuwe stijl: toestand als woord en vorm, cartknoppen van 96 px | gelijk aan het ontwerp "Spelers" (Standby/PFL: toestand als woord en teken, grote resttijd, één gevulde hoofdknop; cartknoppen 96 px met "speelt" en "sleep hierheen") |
| ✅ | Programmalog in de nieuwe stijl | gelijk aan het ontwerp "1 scherm" (tijd, kleurstreep per soort, titel met soort en details, lengte; knoppen pas bij aanwijzen of selecteren) |
| ✅ | Werkruimtes Productie, Planning en Rapportage naast Live, DJ en Automatisering | elke werkruimte opent zijn eigen indeling (zes knoppen in de balk; Live houdt alleen het live-werk, beheervensters staan in hun eigen werkruimte; een oude Standaard-indeling blijft bewaard als "Standaard (oud)") |
| 🟡 | Responsive gedrag en indelingen voor 1 tot 4 schermen, aanraakmodus voor een aanraakscherm | op 1 tot 4 schermen staat alles op zijn plek en blijft dat na een herstart (werkbladen per scherm klaar en getest op 2 schermen: menu Werkbladen, bewaard met de indeling, terug op hetzelfde scherm; aanraakmodus klaar in Instellingen › Uiterlijk) |
| 🟡 | Overige vensters nalopen (planner, bibliotheek, mixer, beheer) — beheervensters krijgen de ruimte in hun eigen werkruimte; cue-editor loopt niet meer over zijn tekst heen. Reclame, nieuws, hardware, databronnen, log-import en klokken: lijst naast het formulier, in een smal paneel (onder 600 px) de lijst erboven. Mengkanalen in de mixer met de knoppen twee bij twee (REC valt niet meer weg); kleine knoppen overal even hoog en afgerond. Nog open: nalopen per venster op een echt studioscherm | geen venster meer in de oude stijl |
| 🟡 | **Cosmetische ronde over de hele studio:** in een smal of laag paneel moet je nu zoeken waar iets staat (bijv. "Mappen en shares" in de Bibliotheek pas na scrollen). Duidelijk laten zien dat er meer is (zichtbare schuifbalk, "meer ▾", inklapbare blokken), beheer naar een eigen tabblad of venster, en per paneel nalopen wat er bij de standaardindeling in beeld past — Bibliotheek: beheer (categorieën, mappen en shares) op een eigen tabblad; mixer: fader schaalt mee met het paneel zodat ZENDER in beeld blijft; beheervensters in hun eigen werkruimte. Nog open: nalopen op het echte studioscherm | in de standaardindeling is alles vindbaar zonder te weten dat je moet scrollen |
| ✅ | Uiterlijk instelbaar: meerdere thema's (donker, licht en meer), afgeronde vormen, lettergrootte, aanraakmodus, kleuren per soort item (vraag 7) | in Instellingen te kiezen, direct zichtbaar (Instellingen › Uiterlijk: vier stijlen, drie hoekvormen, grootte 80–150 % voor alle vensters en werkbladen, aanraakmodus met knoppen en regels van minstens 44 px, een kleur per soort item voor log, as-run en bibliotheek) |
| ✅ | Eigen knoppen, regiekamer-venster (weer, nieuwskoppen, berichten), itemgegevens, verkenner | vensters aanwezig en te docken (Eigen knoppen: knoppenwand die elk een engine-commando uitvoert, met naam, commando en kleur, bewaard per studiocomputer; Itemgegevens: alles over het item ON AIR of de selectie; Verkenner: mappen van de engine-computer bladeren, favorieten en bibliotheekmappen, audiobestand als volgende/achteraan in de log, in PFL of slepen, ook buiten de bibliotheek; Regiekamer: tijd en datum, ON AIR en straks, weer (Open-Meteo, dezelfde plaats als de presentator) en nieuwskoppen uit de presentator-feeds, laatste berichten) |
| 🟡 | **Stijlupdate uit het stijldocument (11 november 2026):** twee richtingen uitgewerkt in het ontwerp "Stijlupdate REA-Radio-DJ" (Regiekamer en Daglicht, met paletten, soortkleuren, letters en regels voor smalle ruimtes). Gebouwd: stijlen **Regiekamer** (nieuwe standaard) en **Daglicht** in Instellingen › Uiterlijk; in een lichte stijl worden de standaard soortkleuren vanzelf donkerder zodat ze op wit leesbaar zijn; lege pads en uit-lampjes volgen de stijl. Vensters volgen hun eigen breedte (breed vanaf 900, middel 560–899, smal daaronder): de programmalog vouwt de detailregel en opdrachten in (menu ⋯) en laat in een laag paneel eerst de nu-kaart en dan "Gespeeld" weg; de cartwall krijgt minder kolommen in plaats van kleinere tegels; de bibliotheek zet bewaarde lijsten, filters en status in een smal of laag paneel onder de knop "Filters ▾" en laat de lijst nooit meer over het zoekgedeelte lopen; de appbalk laat naam, verbindingstekst en indelingskeuze vallen en zet de werkruimtes onder 1100 px op een tweede regel. **Ctrl+K** (ook Studio › Ga naar venster…) opent een venster door een deel van de naam te typen. Nagekeken met schermafbeeldingen op 1920 × 1080 en 1000 × 700. Nog open: Ctrl+K met de hand proberen, de rest van de vensters (spelers, live-decks, cue-editor) op smalle breedtes, vaste kleuren in oudere vensters (bijv. de donkere golfvormvlakken), schakelaar "rustige kleuren" | Regiekamer en Daglicht zijn te kiezen en in een klein paneel valt niets over elkaar heen |

Vragen bij stap 3:

6. ~~Hoeveel schermen?~~ **3 tot 4, met eventueel een aanraakscherm.**
7. ~~Licht thema?~~ **Ja, meerdere thema's met afgeronde vormen en goede bedienbaarheid.**

## Stap 4 — DJ-modus afmaken

| Status | Werk | Af als |
|---|---|---|
| ⬜ | Luistertest van wat er staat: tempo, sync, loops, overnemen; bijstellen van vertraging en overgang | overnemen is onhoorbaar; een loop van 4 beats loopt strak |
| ✅ | Twee doorlopende golfvormen boven elkaar met beatstrepen | beats van twee decks zijn op het oog gelijk te leggen (boven in de DJ-tafel: 2 of 4 decks onder elkaar, afspeelpunt in het midden, dunne streep per tel en dikke per maat, tijdas met het tempo erin zodat gesynchroniseerde decks op elkaar liggen; muiswiel zoomt 2 tot 30 s. Getest met A en B na sync op 124 BPM) |
| ✅ | Toonsoort meten; BPM, toonsoort en ReplayGain al bij het inlezen, zichtbaar in de bibliotheek: BPM en beatgrid worden bij het inlezen gemeten (`bpm:120-128`); de toonsoort komt uit de tag (TKEY/INITIALKEY) of wordt gemeten (chroma met Krumhansl-profielen), oudere nummers worden op de achtergrond nagemeten; zichtbaar als Camelot en naam (8A · Am), te zoeken met `toon:8A` en `toon:8A+` (ook wat er harmonisch bij past) en te sorteren; ReplayGain (naar -18 LUFS) staat in het detailpaneel. De meting is een benadering: met het oor nog niet tegen een grote set nagelopen | bibliotheek sorteert op BPM en toonsoort |
| 🟡 | Quantize, loop-roll, cue-mix op de koptelefoon, begrenzer op de DJ-uitgang | klaar in de DJ-tafel: Quantize (hot cues, loops en rolls op de tel, sprong naar een hot cue blijft in de maat), ROLL-knoppen 1/8 tot 1 tel (ingedrukt houden; bij loslaten loopt het nummer verder alsof het doorspeelde), schuif KOPTELEFOON van cue naar master, begrenzer -1 dBFS op de DJ-uitgang (standaard aan, ook in de mixopname). Op de test-engine nagemeten; nog niet beluisterd. Instellingen worden nog niet bewaard bij een herstart |
| 🟡 | Effecten per deck (echo, flanger, reverb) op de beat | klaar in elk deck (EFFECT): echo met een vertraging van 1/4 tot 8 tellen, flanger met één zwaai per zoveel tellen, galm (meer tellen = grotere ruimte); volgt het tempo, met MIX-schuif en FX AAN, op de zender en in de koptelefoon. Op de test-engine nagemeten (niveau van droog 1,04 naar hooguit 1,34 bij volle mix, de begrenzer vangt de rest); nog niet beluisterd |
| 🟡 | Sampler: cartwall als pads in de DJ-werkruimte: een derde wand (4 pagina's × 16 pads) in het venster Sampler (DJ). Pads spelen via de DJ-tafel: OFF AIR alleen in de koptelefoon, ON AIR via de begrenzer op de zender en in de mixopname; ze dempen de automatisering nooit. Met Quantize wacht een pad op de volgende tel van het leidende deck (binnen 30 ms na een tel start hij meteen). Eigen stopknop, lus per pad, ook via TCP/OSC (CART 3 n). Getest op de test-engine: 5 van 6 starts op fase 0,00–0,05; nog niet beluisterd | pad start hoorbaar op de tel |
| 🟡 | Automix in de DJ-modus met overgangstypes: venster Automix (DJ) met een wachtrij (slepen uit de bibliotheek, omhoog, weghalen, wissen) die zichzelf op deck A en B speelt. Overgangen Beatmix (tempo en tel gelijk, daarna in 8 s terug naar het eigen tempo; boven 8 % verschil wordt het een fade), Fade, Cut (op de tel), Echo en Filter, 4–32 tellen lang; "Nu mixen" begint meteen. De overgang start op het mixpunt, of eerder als hij anders niet past. Zolang automix aan staat volgt deck A de automatisering niet. Tempo-overgang ook in de automatisering: overgangsregel "op de tel, tempo gelijk" (song → song): het volgende nummer start op een tel, speelt eerst op het tempo van het vorige en glijdt daarna terug. Getest op de test-engine: tien nummers mixten zichzelf (beatmix na uitlijnen 0–1 ms naast elkaar), fade/cut/echo/filter met drie nummers; automatisering 1,041× tijdens de overlap (verwacht 1,043×), daarna 0,999×. Nog niet beluisterd | een lijst van tien nummers mixt zichzelf |
| 🟡 | Automatisering loopt onhoorbaar door terwijl de DJ op de lucht is, met een oplichtend veld en één knop om terug te geven (vraag 9): DJ ON AIR sluit het log- en overlaykanaal in plaats van de automatisering te stoppen; in de DJ-mengtafel licht een veld op met wat er onhoorbaar loopt, hoe lang nog en wat daarna komt, met de knop "Teruggeven aan automatisering" (ook Alleen PFL geeft terug). Wat onhoorbaar begon telt niet als uitgezonden: geen as-run en geen uitzendbewijs voor reclame; na teruggeven komt het lopende nummer vanaf dat moment in de as-run. Is de automatisering intussen gestopt, dan start teruggeven hem weer. Getest op de test-engine (doorlopen, volgend nummer onhoorbaar, teruggeven, gestopt en terug); nog niet beluisterd | DJ van de lucht → automatisering is meteen hoorbaar op de juiste plek |
| 🟡 | Bediening van decks en mengpaneel via de commandobus: MIDI leren en voorinstellingen voor Hercules en Pioneer, ook oudere typen (vraag 8). Commando's DJ <A-D> <actie>, DJ XFADER, DJ CUEMIX, DJ ONAIR en DJ AUTOMIX op de commandobus (dus ook via TCP/OSC). Venster DJ-controller: MIDI-poort toevoegen, per functie "Leren" en de knop, fader of het jogwiel bewegen; 77 functies (mengpaneel en per deck start, cue, sync, PFL, tempo, jogwiel, kanaalfader, gain, EQ, filter, loop, effect, hot cue 1–4); faders omkeren; bewaren als voorinstelling en die op een andere controller zetten. Het tempo volgt het bereik van het deck, een EQ-knop in zijn klik staat precies vlak. Getest op de test-engine met nagebootste MIDI-signalen (leren, start/pauze, cue, tempo, EQ, crossfader, jogwiel, voorinstelling, bewaard na herstart). Nog niet met een echte controller; ingebouwde voorinstellingen voor Hercules en Pioneer wachten op de officiële MIDI-overzichten van de typen die de omroep gebruikt | een controller bedient start, cue, tempo, EQ en crossfader |
| ✅ | Mix opnemen | opname staat als bestand in de bibliotheek (knop "● Mix opnemen" in de DJ-tafel; op en van de lucht; na stoppen in Opnamen) |
| 🟡 | Draaiknoppen in plaats van schuiven, en het geheel passend op 1920 × 1080 zonder scrollen | met 2 decks klaar: gain, filter, EQ, effectmix en koptelefoon zijn draaiknoppen; past op 1920 × 1080. 4 decks nog nalopen |
| 🟡 | Censuur, rem en backspin per deck (uit de functievergelijking, 11 november 2026): **Censuur** ingedrukt houden laat het nummer achteruit klinken om een woord te maskeren; bij loslaten loopt het verder waar het gebleven zou zijn, alsof het nooit stil stond. **Rem** laat het deck in 1,5 s uitlopen tot stilstand zoals een draaitafel die je uitzet, **Backspin** gooit de plaat achteruit en laat hem uitlopen; daarna staan beide op pauze. In elk deck in de rij STOP, ook via MIDI (leren: Censuur, Rem, Backspin) en de commandobus (DJ A CENSOR ON/OFF, DJ A BRAKE [s], DJ A BACKSPIN [s]). Getest op de test-engine: 2 s censuur kwam terug op 5,13 s (verwacht 5,12), rem en backspin eindigen op pauze, daarna speelt het deck weer op precies 1,0×. Nog niet beluisterd | een gevloekt woord is live weg te drukken zonder dat het nummer verspringt |
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
| 🟡 | Opnemen van programma en microfoon; automatisch per uur met bewaartermijn (instelbaar, standaard een jaar); direct opnemen naar de bibliotheek — klaar in Instellingen › Opnemen: de uitzending als MP3 per klokuur (getest), de microfoon apart per keer dat hij open is, optioneel direct in de bibliotheek (categorie Opnamen); bewaartermijn ruimt alleen eigen opnamen op. De microfoonopname nog niet met een echte microfoon getest. |
| 🟡 | Meerdere streamservers tegelijk: Icecast en SHOUTcast v1/v2; MP3, AAC/HE-AAC, Ogg Vorbis, Opus en FLAC; HLS — klaar: naast de hoofdstream zoveel extra servers als nodig (Icecast, SHOUTcast v1/v2 via het adres), elk met eigen MP3-kwaliteit en de titel; een weggevallen stream verbindt zichzelf opnieuw (getest met twee mounts tegelijk). Nog open: AAC, Ogg, Opus en FLAC (vragen om extra BASS-encoders) en HLS. |
| ✅ | Titelupdates: eigen opmaak met velden ({artiest} {titel} {categorie} {duur} {station} …), per soort kiezen (muziek, jingles, gesproken, AI-presentator, reclame) met een eigen tekst voor de rest, naar de stream, een tekstbestand en een webadres (Stream › Titel en programmalog) |
| ✅ | Programmalog als tekstbestand met eigen opmaak en bewaartermijn: één bestand per dag (jjjj-mm-dd.txt), ook cartwall en overlays; oudere dagbestanden gaan na de bewaartermijn weg |

Vragen bij stap 6:

13. ~~Streamservers?~~ **Alle gangbare servers en formaten.**
14. ~~Bewaren?~~ **Instelbaar, standaard een jaar.**

## Stap 7 — Bibliotheek en planning

| Status | Werk |
|---|---|
| ✅ | Meer dan twee bibliotheekmappen, automatisch opnieuw inlezen, vervangende locaties — Zelf mappen en shares toevoegen, bewaken en verwijderen; vervangende locaties (ontbreekt een bestand, dan zoekt de engine het onder de vervangende map, bij spelen, voorluisteren, hoezen en hervatten) en paden definitief omzetten met behoud van alle gegevens (Bibliotheek → Mappen en shares). |
| ✅ | Tags lezen en terugschrijven, hoezen — Markers, beatgrid en BPM gaan per map optioneel het bestand in (ID3v2 TXXX "REA-RADIO-DJ" of Vorbis-commentaar REA_RADIO_DJ, plus de gewone BPM-tag) en worden bij het inlezen teruggelezen; Op verzoek gaat alles wat de bibliotheek weet het bestand in (artiest, titel, album, genre, jaar, taal, sfeer, waardering, BPM, toonsoort, ISRC, componist, label, ReplayGain), per nummer of voor een selectie; hoes kiezen of weghalen in het detailvenster. |
| ✅ | Overgangen en mixpunten per soort item en per nummer — per nummer met de cue-editor en het overgangsvenster; per soort (nummer, jingle, stem, voicetrack, AI-break, reclame) en per categorie met regels in Instellingen › Mix: automatisch, achter elkaar, overlap of uitfaden in N seconden. De bovenste passende regel telt; een handgemaakte overgang gaat voor. |
| ⬜ | Centrale bibliotheek voor meerdere studio's |
| ✅ | Rapportage auteursrechten in een eigen indeling (CSV en PDF); er is geen voorgeschreven sjabloon — in As-run: elke uitzending of een overzicht per nummer, als CSV of PDF (A4 liggend, met periode, totalen en ontbrekende ISRC/componist). |
| ✅ | Reclame-afspeelrapport per adverteerder en campagne, als bewijs voor klanten en stichtingen; export als CSV en JSON om later te koppelen: Reclame › Uitzendbewijs filtert op periode, adverteerder of campagne, toont totalen (gepland, uitgezonden, volledig, gemist, zendtijd) en exporteert het uitzendbewijs (CSV), het overzicht (CSV) en alles samen (JSON) |

### Bibliotheek: slimmer en gebruiksvriendelijker

Gevraagd op 10 november 2026: de bibliotheek bekijken naast die van een professioneel playoutpakket, en hem slimmer en makkelijker maken, met het oog op de gebruiker.

**Wat dat pakket kan (alleen als controlelijst van functies):**
- bibliotheken uit mappen, afspeellijsten, een database of een aparte bibliotheekserver, meerdere tegelijk;
- alles in het geheugen;
- snel zoeken en een zoekopbouw met velden;
- veel tagvelden, waardering, ReplayGain en hoezen;
- per nummer veel cuepunten, hotstarts en gebeurtenissen;
- mappen bewaken of op een vast moment opnieuw inlezen.

**Zwakke punten volgens gebruikers op het openbare forum:**
- nieuwe bestanden worden pas gevonden na een handmatige "rescan";
- tags worden pas gelezen als een nummer een keer getoond is, dus zoeken mist nummers;
- geforceerd tags lezen is te traag voor live gebruik;
- "herladen" en "opnieuw inlezen" zijn verwarrend;
- herladen gooit niet-opgeslagen wijzigingen weg.

Daar willen we beter in zijn. We nemen geen namen, schermen, teksten of bestandsformaten over; dit is een eigen ontwerp.

**Wat we al hebben:**
- één bibliotheek met categorieën uit submappen;
- bij het inlezen worden alle tags, de cues en LUFS meteen gemeten;
- zoeken op artiest, titel, album en categorie;
- aan/uit en geldig van/tot;
- hoe vaak en wanneer een nummer gespeeld is;
- slepen naar de log, de spelers en de cartwalls.

| Status | Werk | Klaar als |
|---|---|---|
| ✅ | **Altijd volledig zoeken:** alle tags worden bij het inlezen in de database gezet, dus zoeken vindt elk nummer meteen; nooit "eerst tonen, dan pas vindbaar" Klaar: tags staan bij het inlezen in de database en de mappen worden bewaakt. Getest: een nieuw bestand was na 4,6 s vindbaar. | een net toegevoegd nummer is binnen enkele seconden vindbaar op album |
| ✅ | **Slim zoeken:** zonder accenten en hoofdletters ("beyonce" vindt Beyoncé), kleine tikfouten mogen, woorden over alle velden ("adele 2015"), beste treffer bovenaan Klaar: zonder accenten, één tikfout bij 4-7 letters en twee vanaf 8, omgewisselde letters, "acdc" vindt AC/DC, beste treffer bovenaan, ook een woordgroep tussen aanhalingstekens en -woord om uit te sluiten. | een tikfout in de artiest vindt toch het nummer |
| ✅ | **Zoeken met korte filters in het zoekvak:** bijvoorbeeld `bpm:120-128`, `jaar:198`, `duur:<3:30`, `cat:nederlandstalig`, `niet:gespeeld`, met uitleg als je `?` typt Klaar: `artiest:` `titel:` `album:` `cat:` `jaar:` `duur:` `gespeeld:` en `is:` (nieuw, nooit, lang, zondercues, ontbreekt, verlopen, uit, mag, geblokkeerd, muziek, jingle); `?` of de ?-knop toont de hulp. `bpm:` volgt zodra BPM bij het inlezen gemeten wordt (stap 3). | de filters werken en staan in de hulp |
| ✅ | **Filterknoppen boven de lijst:** categorie, decennium, duur, BPM, nieuw toegevoegd, lang niet gespeeld, zonder cues, ontbrekend bestand, verlopen; één klik zet een filter aan of uit Klaar: Mag nu, Nieuw, Nooit gespeeld, Lang niet gespeeld, Korter dan 3:30, Zonder cues, Aandacht nodig, Ontbreekt, plus een keuzelijst voor het decennium. De knoppen zetten hun filter in het zoekvak, zodat je de korte filters vanzelf leert. | met twee klikken staat "nieuw en nog nooit gespeeld" in beeld |
| ✅ | **Slimme lijsten:** een zoekopdracht met filters bewaren onder een eigen naam ("Nieuw deze maand", "Lang niet gespeeld"); de lijst werkt zichzelf bij Klaar: "Bewaarde lijsten…" boven de bibliotheek, met "Bewaar…" voor de zoekopdracht, categorie en sortering van nu; bij elke lijst staat hoeveel nummers er nu in zitten. Drie staan er al klaar: Nieuw deze maand, Lang niet gespeeld, Aandacht nodig. Bewaard in de database, dus voor alle studio's. Getest: na een herstart telt de lijst ook het nieuwe nummer mee. | een bewaarde lijst toont na een herstart de nieuwe stand |
| ✅ | **Mag dit nu?** Bij elk nummer een kleine markering of het nu ingepland mag worden, en zo niet, waarom: "artiest 40 min geleden gespeeld", "verlopen op 1-11", "bestand ontbreekt", "uitgezet" Klaar: met dezelfde regels als de automatische keuze (laatste N nummers en artiesten, ook wat al gepland is). Een gele regel bij het nummer geeft de reden; de lijst ververst als er een nieuw nummer begint. | de reden staat bij het nummer en klopt met de rotatieregels |
| ✅ | **Detailpaneel naast de lijst:** golfvorm met de cues en de gemeten markers, tags, LUFS, BPM, waarom het nu wel of niet mag, de laatste keren gespeeld; tags direct aanpassen (ook in het bestand); de hoes uit het bestand of cover.jpg/folder.jpg ernaast; toonsoort en ReplayGain; in een smal paneel verdwijnt het vanzelf zodat de lijst ruimte houdt | een tag aanpassen zonder een apart venster |
| ✅ | **Kolommen of kaarten:** sorteren (beste treffer, artiest, titel, jaar, BPM, toonsoort, duur, laatst/vaakst gespeeld, nieuwste) en grote regels voor het touchscreen; onder "Velden ▾" zelf kiezen welke gegevens onder de titel staan (17 velden, o.a. toonsoort, ReplayGain, waardering, energie, bitrate) en in welke volgorde; de keuze wordt onthouden | op het touchscreen zijn de regels groot genoeg voor een vinger |
| ✅ | **Meerdere nummers tegelijk:** selecteren met Shift en Ctrl en dan in één keer de categorie, aan/uit of de geldigheid aanpassen | 20 nummers in één keer naar een andere categorie |
| ✅ | **Snelle bediening:** rechtsklikmenu met alle acties; sneltoetsen (Enter = volgende in de log, spatie = voorluisteren, Ctrl+Enter = achteraan); wat dubbelklikken doet is instelbaar | alles kan zonder muis |
| ✅ | **Voorluisteren in de lijst:** spatie, het menu of de ▶-knop op elke regel luistert voor op de PFL (nog eens klikken of Esc stopt); klikken in de golfvorm van het detailpaneel luistert vanaf dat punt, met de afspeelpositie zichtbaar | een refrein beluisteren zonder het nummer te laden |
| ✅ | **Mappen bewaken:** nieuwe, gewijzigde en verwijderde bestanden worden vanzelf verwerkt, zonder knop; daarnaast elke nacht een controle; geen verschil meer tussen "herladen" en "opnieuw inlezen" Klaar: 4 s na de laatste wijziging wordt alles verwerkt; een bestand dat nog gekopieerd wordt komt erbij zodra het klaar is; elke nacht om 4 uur een volledige controle (instelbaar). Getest: verwijderd bestand na 4,6 s gemarkeerd als ontbrekend. | een nieuw bestand in de map staat zonder klikken in de bibliotheek |
| ✅ | **Verplaatste bestanden terugvinden:** een ontbrekend bestand wordt gezocht op naam, grootte en duur in de bewaakte mappen; cues, categorie en geschiedenis blijven behouden Klaar via het herkenningspatroon: het luidheidsverloop per seconde moet in lengte, vorm (correlatie ≥ 0,9) en niveau overeenkomen; een vlak verloop wordt nooit herkend. Getest: een hernoemde mp3 in een andere map kreeg zijn id, cues en geschiedenis terug. | een hernoemd of verplaatst nummer houdt zijn cues |
| ✅ | **Wijzigingen gaan nooit verloren:** alles wordt direct opgeslagen; met "ongedaan maken" zet je de laatste wijziging terug Klaar: categorie, aan/uit, geldigheid, tags en cues; ook voor meerdere nummers tegelijk. Knop "↶ Ongedaan maken: …" en Ctrl+Z in de bibliotheek; de laatste 200 wijzigingen worden bewaard, ook na een herstart. Getest: een verkeerd verplaatst nummer staat met één klik terug. | een verkeerde categoriewijziging is met één klik terug |
| ✅ | **Kwaliteitscontrole bij het inlezen:** te zacht of te hard, clipping, lange stilte aan het begin of eind, lage bitrate, dubbele nummers; een lijst "aandacht nodig" Klaar: te zacht (onder -23 LUFS) of te hard (boven -5), clipping (platte toppen op vol niveau, 20 keer per minuut of meer), stilte van 3 s aan het begin of 6 s aan het eind, mp3/ogg onder 128 kbps, en dubbel (zelfde artiest en titel, ook "(Radio Edit)", lengte binnen 3 s). Een oranje regel in de lijst, een blok in het detailpaneel, de filterknop "Aandacht nodig" en `is:aandacht`, `is:dubbel`, `is:stil`, `is:clipping`, `is:zacht`, `is:hard`, `is:bitrate`. Oudere nummers worden na een scan alsnog nagekeken. Op de testmuziek: alleen één echt te zachte jingle gemeld; luide commerciële masters geven geen valse "clipping". | een dubbel nummer en een stil begin worden gemeld |
| ✅ | **Meer tagvelden:** jaar, genre, taal, energie, stemming, waardering, ISRC, componist en uitgever (ook voor de rapportage); zie ook "Tags lezen en terugschrijven, hoezen" Klaar: taal, stemming en waardering worden uit de tags gelezen (ID3 TLAN/TMOO/POPM, Vorbis LANGUAGE/MOOD/RATING) en bij "Markers in het bestand" ook teruggeschreven; energie (1 rustig tot 5 knallen) is je eigen inschatting. Alles staat in het detailpaneel en is doorzoekbaar: `taal:nl`, `stemming:zomer`, `waardering:>=4`, `energie:4-5`. Hoezen staan nog open (detailpaneel). | de velden staan in het detailpaneel en zijn doorzoekbaar |
| ✅ | **Zelf mappen en shares kiezen:** in de Bibliotheek onder "Mappen en shares" een map op deze pc, een andere schijf of een share (\\server\muziek) toevoegen met "Bladeren…" of een pad; per map muziek of jingles, een vaste categorie of submappen als categorie, bewaken aan/uit, aan/uit. Overlappende of onbereikbare mappen worden geweigerd met een duidelijke melding. Een share die later offline is, laat zijn nummers niet "ontbreken" | een share toevoegen, inlezen en weer verwijderen |
| ✅ | **Alle metadata overnemen:** artiest, titel, album, albumartiest, genre, jaar, ISRC, componist, uitgever en een BPM-tag uit het bestand | de velden staan in de database |
| ✅ | **Beat en verloop meten bij het inlezen:** BPM en beatgrid (BASS_FX), het luidheidsverloop per seconde, en geschatte intro- en outromarkers (waar het nummer op niveau komt en waar het einde begint); oudere nummers worden na een scan alsnog gemeten | BPM en markers bij elk nummer; getest met echte mp3's (bijv. 127,95 en 120,05 BPM) |
| ✅ | **Bewaren in de database of in het bestand:** standaard alles in de database; per map kan "Markers in het bestand" aan, dan gaan cues, beatgrid en BPM ook in de tags, zodat ze met het bestand meegaan naar een andere pc of station | een andere bibliotheek leest de markers uit het bestand terug |
| ✅ | **Verwijderen zonder verlies:** een verwijderde map haalt de nummers uit beeld, maar alle gegevens (cues, geschiedenis, beat, rechten) blijven bewaard; komt een nummer terug, in dezelfde of een andere map en ook onder een andere naam, dan wordt het herkend aan zijn eigen verloop | verwijderen en terugzetten verliest niets |
| ✅ | **Snel bij grote bibliotheken:** zoeken blijft onder 100 ms bij 100.000 nummers; de lijst laadt alleen wat in beeld staat Klaar: de zoekvelden worden één keer per nummer klaargezet, tikfouten één keer per woord uitgezocht en de nummers op alle kernen nagelopen. Gemeten met 100.000 nummers: 5 tot 83 ms (tikfout "Daivd Guetta" 65 ms). De lijst tekent alleen de regels die in beeld staan. | gemeten met een testbibliotheek van 100.000 nummers |

Volgorde:
1. Eerst altijd volledig en slim zoeken, mappen bewaken, en "mag dit nu?". Daar verdient de gebruiker het meest mee.
2. Daarna het detailpaneel, de kolommen of kaarten, meerdere nummers tegelijk, en de sneltoetsen.
3. Daarna de slimme lijsten, de kwaliteitscontrole en de verplaatste bestanden.

Vraag bij stap 7:

15. ~~Sjabloon?~~ **Geen; eigen indeling, plus een los reclame-afspeelrapport.**

### Nog niet gebouwd, uit de functievergelijking (11 november 2026)

Wat het onderzoek naar volwaardige playout-pakketten noemde en wij nog niet hebben. Alleen als functielijst gebruikt: eigen ontwerp en eigen namen.

| | Wat | Klaar als |
|---|---|---|
| ⬜ | **Containers:** één regel in de log die een groep nummers bevat (bijv. een reclameblok of een vaste jingle-set), die als één geheel te verslepen, te kopiëren en over te slaan is en bij afspelen openklapt | een reclameblok staat als één regel in de log en speelt zijn nummers achter elkaar |
| ⬜ | **Gebeurtenissen op een plek in een nummer:** op een tijdstip in een nummer een volume-omhullende (bijv. zachter onder een intro-tekst) of een opdracht (bijv. lampje aan, overlay start); te zetten in de cue-editor | een opdracht op 0:12 in een nummer gaat elke keer op 0:12 af |
| ⬜ | **Klembord voor nummers:** een vast vak waar je nummers uit bibliotheek, log of decks in parkeert en later weer uitsleept, ook tussen werkbladen | een nummer gaat via het klembord van de bibliotheek naar deck B |
| ⬜ | **Uitzendstatistiek:** uit de as-run per dag, week en maand: meest gedraaide nummers en artiesten, rotatie per categorie, uren muziek tegen spraak, wat lang niet gedraaid is | een maandoverzicht laat de top 20 en de rotatie per categorie zien |

### AI-presentator: creatief

| | Wat | Klaar als |
|---|---|---|
| ✅ | **Creatieve presentator:** praat de plaatjes zelf aan elkaar en snijdt onderwerpen aan, soms serieus, soms grappig, soms leuk, soms een tikkie op het randje. Onderwerpen uit een eigen lijst (met de toon er vast bij als je wilt: "grappig: …"), uit online feeds van de zender (RSS, Atom of een tekstpagina, elk half uur opgehaald) en uit een ingebouwde lijst die past bij dagdeel, weekdag en seizoen. Toon vast of willekeurig naar verhouding (bijv. 1-2-3-1); een kans per break; hetzelfde onderwerp niet binnen 12 uur. Eigen stem te kiezen (Higgs of een andere stem); bij Higgs klinkt de toon ook in de stem. Vaste grenzen voor elke toon: niets kwetsends, geen politiek, niets grofs, geen verzonnen nieuws. Een bericht om voor te lezen gaat altijd voor. In de studio: AI-presentator › Creatieve presentator, met "Onderwerpen bekijken", "Proefscript" en "Proef met stem (PFL)". Getest met qwen3.8:27b en Higgs: vier tonen, een onderwerp uit de RSS-feed, en een gewone break in de log die zelf een onderwerp koos en ingesproken klaarstond | een break in de log kiest zelf een onderwerp en toon en wordt met de gekozen stem ingesproken |
| ✅ | Denkende taalmodellen (qwen3, deepseek-r1): de presentator vraagt ze niet eerst te "denken", anders bleef de tekst leeg; vreemde (Chinese) woorden worden eruit gehaald. Een proef laat nu zien waarom het taalmodel niets gaf | een proef met qwen3.8 geeft tekst in plaats van de sjabloon |

## Stap 8 — Bediening en hardware

| Status | Werk |
|---|---|
| ✅ | Gebeurtenissen koppelen aan commando's (start van een item, microfoon open, stream weg): Bediening › Gebeurtenissen; ook per soort item (nummer, jingle, AI-presentator, voicetrack), microfoon dicht en stream terug, met {artiest} {titel} {type} {categorie} {station} in het commando |
| ✅ | Bediening via een webadres per knop en een UDP-poort; sneltoetsen die ook buiten de studio werken: /api/do/next, /api/do/cart/1/5 of /api/do?text=… (met wachtwoord ?key=…); UDP-poort 8603 met één commando per pakket en antwoord terug; per sneltoets 'overal' (Windows RegisterHotKey) |
| ✅ | OSC uitsturen: commando OSC <host:poort> </adres> [argumenten] (getallen als i/f, tekst als s), samen met HTTP <webadres> te gebruiken bij gebeurtenissen, sneltoetsen en de planner |
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
| 🟡 | Installatieprogramma, controle op nieuwe versies, pagina Over — pagina Over klaar: opstartscherm en Info › Over REA-Radio-DJ (gemaakt door ICT Holland, DVC-it; versie, licentie, BASS, broncode). Installatieprogramma en versiecontrole nog open |
| ⬜ | Handleiding voor de studio en voor de beheerder |

## Wat niet op de lijst staat

- Cd's rippen en branden.
- Inloggen op of doorzetten van de muziekcatalogus van een dj-pakket.
- De eigen codec van LUCI (ULCC).
