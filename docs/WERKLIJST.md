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

## Proof of concept

De proof of concept is af na **stap 1 tot en met 5**: een station dat niet stilvalt, geluid dat naar de juiste kaarten
gaat, één herkenbare stijl, een DJ-tafel waarop je echt kunt mixen en één werkende live-verbinding. Stap 6 tot en met 10
maken er een volledig product van.

## Stap 1 — Bedrijfszeker

Eerst zorgen dat de zender niet stil kan vallen; alles daarna bouwt daarop.

| Status | Werk | Af als |
|---|---|---|
| 🟡 | Stiltedetectie op de zender: drempel, duur en actie (melden, of ook de automatisering starten) staan, in te stellen bij Instellingen › Bewaking. Nog open: een noodcategorie spelen | stilte van de ingestelde duur geeft een alarm en de gekozen actie; getest met een stille engine |
| ✅ | Watchdog: de engine schrijft een hartslag; een klein bewakingsproces start hem opnieuw na een crash of vastloper, en hij gaat verder waar de log was | engine geforceerd stoppen → binnen 15 s weer op de lucht met dezelfde log. Getest: na 1,4 s terug, hetzelfde nummer op dezelfde plek. Een vastloper (engine reageert niet meer) is gebouwd maar niet nagebootst |
| 🟡 | Engine bij aanmelden starten op de studio-pc, los van de studio (dienst later): Instellingen › Systeem | na herstart van de pc speelt het station zonder dat iemand iets doet. Aan- en uitzetten getest; een echte herstart van de pc nog niet |
| ✅ | Reservekopie van instellingen, klokken, planning, cartwalls en hardware in één bestand; terugzetten | kopie terugzetten op een lege datamap geeft hetzelfde station |
| ✅ | Systeemlog van de engine in de studio, met niveau gewoon of uitgebreid | fouten zijn in de studio terug te lezen zonder in bestanden te zoeken |

Vragen bij stap 1:

1. Wat moet er bij stilte gebeuren: alleen melden, de automatisering starten, of een vaste noodcategorie spelen? (Standaard: automatisering starten na 20 s onder -45 dB, en melden.)
2. ~~Draait de engine straks op dezelfde pc als de studio?~~ **Ja, dezelfde pc.**

## Stap 2 — Geluid en mengpaneel

| Status | Werk | Af als |
|---|---|---|
| ⬜ | Uitgang per mengkanaal: automatisering, standby, cartwalls, overlays, DJ, VoIP en voorluisteren elk naar een eigen kaart of kanaalpaar; meerdere uitgangsbussen | elk kanaal is in Instellingen op een eigen uitgang te zetten en blijft daar na een herstart |
| ⬜ | Ingangen als mengkanaal: meer dan één microfoon, lijn-in (cd-speler, tweede studio), met doorzetknop | een lijn-ingang staat als kanaal in de mixer en kan op de zender |
| ⬜ | ASIO-uitvoer en instelbare buffer en samplerate, met testtoon per uitgang | ASIO-kaart speelt zonder haperen; testtoon klinkt op de gekozen uitgang |
| ⬜ | Kanaalstrip per mengkanaal: gain, balans, equalizer, compressor, automatische niveauregeling, dempen, naar de zender, opnemen | instellingen per kanaal hoorbaar en bewaard |
| ⬜ | Master: begrenzer en loudness-meter op de zender | pieken boven de grens worden begrensd; LUFS zichtbaar |
| ⬜ | Plekken voor VST-effecten per kanaal | één VST-plug-in laadt en werkt op een kanaal |

Vragen bij stap 2:

3. ~~Welke geluidskaart(en)?~~ **Instelbaar: Windows-geluid en ASIO.** Nog open: hoeveel uitgangen gaan naar de mengtafel?
4. Mengt de studio straks in de software (alles op één uitgang) of op een fysieke mengtafel (elke speler een eigen fader)? Welk merk en type tafel?
5. Zijn VST-plug-ins nodig voor de proof of concept, of is de ingebouwde equalizer, compressor en begrenzer genoeg? (Standaard: ingebouwd; VST later.)

## Stap 3 — Eén stijl in de hele studio

| Status | Werk | Af als |
|---|---|---|
| ⬜ | Spelers en cartwall in de nieuwe stijl: toestand als woord en vorm, cartknoppen van 96 px | gelijk aan het ontwerp "Spelers" |
| ⬜ | Programmalog in de nieuwe stijl | gelijk aan het ontwerp "1 scherm" |
| ⬜ | Werkruimtes Productie, Planning en Rapportage naast Live, DJ en Automatisering | elke werkruimte opent zijn eigen indeling |
| ⬜ | Responsive gedrag en indelingen voor 1 tot 4 schermen, aanraakmodus voor een aanraakscherm | op 1 tot 4 schermen staat alles op zijn plek en blijft dat na een herstart |
| ⬜ | Overige vensters nalopen (planner, bibliotheek, mixer, beheer) | geen venster meer in de oude stijl |
| ⬜ | Uiterlijk instelbaar: lettergrootte, aanraakmodus, licht thema, kleuren per soort item | in Instellingen te kiezen, direct zichtbaar |
| ⬜ | Eigen knoppen, regiekamer-venster (weer, nieuwskoppen, berichten), itemgegevens, verkenner | vensters aanwezig en te docken |

Vragen bij stap 3:

6. ~~Hoeveel schermen?~~ **3 tot 4, met eventueel een aanraakscherm.**
7. Moet er een licht thema komen, of is donker genoeg?

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
| ⬜ | Bediening van decks en mengpaneel via de commandobus (MIDI-controller, sneltoetsen) | een controller bedient start, cue, tempo, EQ en crossfader |
| ⬜ | Mix opnemen | opname staat als bestand in de bibliotheek |
| ⬜ | Draaiknoppen in plaats van schuiven, en het geheel passend op 1920 × 1080 zonder scrollen | — |
| ⬜ | Stems (zang, instrumenten, bas, drums) | later; grootste onderdeel, niet nodig voor de proof of concept |

Vragen bij stap 4:

8. ~~Welke dj-controller?~~ **Hercules en Pioneer.** Nog open: welk type precies (bijvoorbeeld DJControl Inpulse 300, DDJ-400)?
9. Moet de automatisering doorlopen (onhoorbaar) terwijl de DJ op de lucht is, zodat zij het weer kan overnemen? Nu stopt zij bij het overnemen.

## Stap 5 — Verbindingen (codec, VoIP)

| Status | Werk | Af als |
|---|---|---|
| ⬜ | Mengkanaal VoIP met eigen fader, voorluisteren en terugkanaal zonder zichzelf | — |
| ⬜ | RTP in twee richtingen met Opus, G.722, G.711 en PCM; jitterbuffer met meting | twee engines praten met elkaar over het netwerk |
| ⬜ | SIP volgens EBU N/ACIP: bellen, gebeld worden, registreren | een verbinding met een telefoon-app op een open codec |
| ⬜ | Venster Verbindingen volgens het ontwerp, met stations en presets | — |
| ⬜ | Bediening en status op de commandobus (knoppen, lampjes, Ember+) | — |
| ⬜ | Later: SRT, twee netwerken tegelijk, Icecast als bron, NDI- of ASIO-ingang, tekstberichten, opname | — |

Vragen bij stap 5:

10. ~~Is er een SIP-account?~~ **Ja, dat kan.** Nog open: welke aanbieder?
11. ~~Waarmee belt een verslaggever in?~~ **Ook LUCI Live** (op een open codec).
12. Is de studio van buiten bereikbaar (vast IP-adres of doorgestuurde poorten), of loopt alles via een SIP-server?

## Stap 6 — Opnemen en uitzenden

| Status | Werk |
|---|---|
| ⬜ | Opnemen van programma en microfoon; automatisch per uur met bewaartermijn; direct opnemen naar de bibliotheek |
| ⬜ | Meerdere streamservers tegelijk, AAC en Opus naast MP3, HLS |
| ⬜ | Titelupdates: eigen opmaak, soorten uitsluiten, ook naar een bestand of webadres |
| ⬜ | Programmalog als tekstbestand met eigen opmaak en bewaartermijn |

Vragen bij stap 6:

13. Naar welke streamservers moet het station, en in welk formaat?
14. Hoe lang moeten uitzendingen bewaard blijven voor de toezichthouder?

## Stap 7 — Bibliotheek en planning

| Status | Werk |
|---|---|
| ⬜ | Meer dan twee bibliotheekmappen, automatisch opnieuw inlezen, vervangende locaties |
| ⬜ | Tags lezen en terugschrijven, hoezen |
| ⬜ | Overgangen en mixpunten per soort item en per nummer |
| ⬜ | Centrale bibliotheek voor meerdere studio's |
| 🟡 | Rapportage auteursrechten afstemmen op het sjabloon van NLPO, Buma/Stemra en Sena |

Vraag bij stap 7:

15. Is er een voorbeeld van het rapportagesjabloon dat het station moet inleveren?

## Stap 8 — Bediening en hardware

| Status | Werk |
|---|---|
| ⬜ | Gebeurtenissen koppelen aan commando's (start van een item, microfoon open, stream weg) |
| ⬜ | Bediening via een webadres per knop en een UDP-poort; sneltoetsen die ook buiten de studio werken |
| ⬜ | OSC uitsturen |
| ⬜ | USB-GPIO-kaarten, joystickpoort, tweede Ember+- en Livewire-apparaat als reserve |
| ⬜ | Mengtafels: D&R, AxelTech, Wheatstone, elk met het openbare protocol van de fabrikant |
| 🟡 | Ember+ testen tegen LUCI Studio en een Lawo-tafel |

Vraag bij stap 8:

16. Welke van deze apparaten staan er werkelijk in de studio? Alleen daarvoor bouw ik een koppeling.

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
