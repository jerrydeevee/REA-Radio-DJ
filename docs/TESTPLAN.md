# Testplan — luistertests en echte apparatuur

De geautomatiseerde tests (`dotnet test`) draaien zonder geluidskaart, mengtafel of telefooncentrale. Wat alleen met
oren of met echte apparatuur te controleren is, staat hier. Elke test heeft een **klaar als**; schrijf de uitkomst in
de tabel onderaan, ook als iets maar half werkt. Een bekende beperking op papier is ook een uitkomst.

## Eerst: veilig testen

- Test **niet op de zender die uitzendt**. Gebruik een tweede pc, of de zender buiten de uitzenduren met de stream uit.
- Start voor een test een **eigen engine** met een eigen datamap en poort, bijvoorbeeld:
  `REA-Radio-DJ.exe --urls http://127.0.0.1:8612 --ReaRadioDj:DataFolder=D:\rea-test` (en een andere TCP-poort dan 8601
  in *Instellingen › Op afstand*).
- Haal bij een test met een echte kaart, tafel of codec eerst alles los wat de uitzending raakt, of zorg dat de
  uitzend-pc op een andere kaart speelt.
- Noteer bij elke test de versie (*Info*), Windows-versie, kaart en stuurprogramma.

## 1. Luistertests

| # | Test | Zo doe je het | Klaar als |
|---|---|---|---|
| L1 | **DJ-modus** | Werkruimte *DJ*: twee nummers laden, tempo gelijk zetten, hot cues, een loop, de sampler, een effect, backspin. Daarna de automatisering laten overnemen. | Geen tikken of gaten, tempo verandert zonder toonhoogte, de automatisering neemt naadloos over. |
| L2 | **Mix opnemen** in de DJ-modus | Opname aan, tien minuten mixen, opname terugluisteren. | De opname klinkt als wat je hoorde, zonder gaten. |
| L3 | **AI-nieuws op de zender** | Een nieuwsblok regionaal en landelijk inplannen op een vaste tijd, met tune en opener/sluiter. | Het bulletin start op tijd, de stem is verstaanbaar, de tune zit eronder en de bronvermelding klopt met de bron. |
| L4 | **Opnemen met bewaartermijn** | *Instellingen › Opnemen*: uitzending per uur aan, bewaartermijn 1 dag. Twee dagen laten lopen. | Een MP3 per klokuur, de microfoon apart, en bestanden ouder dan de termijn zijn weg. |
| L5 | **Alleen het praten opnemen** | Signaaldetectie aan; praten, stil zijn, muziek laten spelen. | Alleen de presentatorpraat staat in de opname, met de inzet erbij (geen afgekapt eerste woord). |
| L6 | **Zachte start met grens** | *Instellingen › Mixen*: grens 30 s. Een zacht nieuwsblok inplannen midden in een lang nummer. | Het nummer faadt na hoogstens 30 s uit en het blok start; bij een nummer dat bijna klaar is speelt het uit. |
| L7 | **Speelduur per soort** | Jingles hoogstens 10 s; korte items onder 5 s niet faden. Een lange jingle en een korte ID laten spelen. | De lange jingle faadt na 10 s, de korte ID speelt helemaal uit. |
| L8 | **Presentatortekst** | Een reclame met tekst in de log zetten. | De tekst staat groot in de Live-balk zolang het item speelt en verdwijnt daarna. |

## 2. Geluidskaart en ASIO

| # | Test | Zo doe je het | Klaar als |
|---|---|---|---|
| A1 | **ASIO-stuurprogramma start niet** | *Instellingen › Geluid*: zender op een ASIO-kaart, kaart loskoppelen, engine starten. | De engine speelt op de Windows-kaart en de studio toont een melding. |
| A2 | **ASIO-kaart valt weg tijdens het spelen** | Zender op een (USB-)ASIO-kaart, spelen, de kabel eruit, na 30 s er weer in. | Binnen enkele seconden een melding "speelt niet meer"; na terugsteken "speelt weer" en de zender klinkt weer zonder herstart. |
| A3 | **Kaart in gebruik door een ander programma** | Een ander programma de ASIO-kaart laten openen, dan de engine starten. | Terugval met melding, of een duidelijke fout; de engine blijft draaien. |
| A4 | **Volume van de kaart bewaken** | Met de Windows-mixer de uitzendkaart zachter zetten of dempen. | Melding en het volume wordt teruggezet. |
| A5 | **Buffer en haperen** | ASIO-buffer klein (64–128) en groot (512+), een uur laten spelen met de pc belast. | Bij welke buffer het hapert staat genoteerd. |

## 3. Echte apparatuur

| # | Test | Zo doe je het | Klaar als |
|---|---|---|---|
| H1 | **SIP bij de aanbieder** | Venster *Verbindingen*: SIP-account van de aanbieder invullen, registreren; bellen, gebeld worden, achter NAT. | Registratie lukt, beide kanten horen elkaar, de beller hoort zichzelf niet terug (mix-minus), ophangen werkt aan beide kanten. |
| H2 | **SIP: gesprek opnemen** | Een gesprek opnemen, met de hand en automatisch. | De opname bevat beide kanten en verdwijnt na de bewaartermijn. |
| H3 | **Ember+** tegen een echte codec of tafel | Venster *Hardware*: Ember+-verbinding, de boom bekijken; een parameter op een knop, een lampje op een toestand. | De boom laadt, een knop zet de parameter en een lampje volgt de parameter; bij verlies van de verbinding komt een melding en herstel. |
| H4 | **Mackie Control** met een echt paneel | Venster *Hardware*: paneel kiezen; faders, dempen, opnemen, display en meters. | Faders bewegen mee met de studio en andersom, het display toont de kanaalnamen, de meters lopen. |
| H5 | **MIDI-tafel en faderstart** | Een fader of knop leren, een lampje toewijzen. | Faderstart start de juiste speler; het lampje volgt. |
| H6 | **Livewire-GPIO** | GPO op een toestand, GPI op een commando. | Contact dicht/open start en stopt het juiste. |
| H7 | **Seriële poort** | Een regel op een commando, een tekst bij aan/uit. | Commando en uitvoer kloppen met wat is ingesteld. |

## Geteste apparatuur

Vul een regel in per apparaat en per functie. **✅** werkt · **🟡** werkt met een beperking (noteer welke) · **❌** werkt
niet · leeg = niet getest.

| Apparaat | Stuurprogramma / firmware | Uitzenden | Opnemen | ASIO | Overig | Getest op / door | Opmerking |
|---|---|---|---|---|---|---|---|
| Behringer UMC404HD 192k | Behringer UMC-driver met UMC ASIO Driver | ✅ via Windows op `OUT 1-2` | ✅ microfoon op ingang 1 via `IN 1-2`, mono uit links | | | 15-11-2026, studio-pc | Mic op één ingang van een paar klinkt zonder mono alleen links: kies *Mono uit links (ingang 1)*. Bij het installeren van de driver verdween de kaart tot een herstart van Windows; daarna kregen de apparaten andere nummers. ASIO nog niet getest |
| iCON V1-M | firmware zonder versie in de USB-naam (≥ 1.26), poort `iCON V1-M` | | | | 🟡 Mackie Control (H4) | 15-11-2026, studio-pc | Verbindt als Mackie Control op poort 1; faders (ook de motor), mute, V-pot, PLAY, meters en display werken. REA schrijft zijn eigen indeling in het paneel (aanraakknoppen met cartnamen, opgeslagen), iMap is daarvoor niet meer nodig. Het jogwiel stuurt MIDI én werkt nog als muiswiel (de iCON is ook een HID-muis) |

## Uitkomsten

| Test | Datum | Versie | Uitkomst | Bevinding |
|---|---|---|---|---|
| H4 | 15-11-2026 | 1.2.0-rc.1 | 🟡 deels | iCON V1-M: verbonden (Mackie Control), kanaalnamen op het scherm, fader 1 en fader 6 bewegen de mixer. Studio → paneel: de motorfader volgt (100 % → 10 % → 100 %), het display toont het percentage, het mute-lampje volgt en meter 1 loopt mee met de muziek. Paneel → studio: MUTE 3 heft dempen op, V-pot 1 draait de balans van kanaal 1, indrukken zet hem in het midden, PLAY geeft NEXT. Jogwiel: stuurt in de huidige stand van de iCON geen MIDI maar werkt als muiswiel (de iCON is ook een HID-muis en -toetsenbord); met iMAP op Mackie/MIDI zetten en dan opnieuw testen (bladeren door de log, PLAY start de keuze). Nog niet getest: rec, solo (geen functie), aanraken van een fader tijdens een wijziging uit de studio. Het Mackie-logboek (Hardware) toont fadergebaren niet, wat testen lastiger maakt. |
| — | 15-11-2026 | 1.2.0-rc.1 | ✅ nagebootst | Touchscreen van de iCON naar de cartwall: noten op kanaal 2–4 gaan naar de ingangen (blauw = kanaal 2 → knop 1–24, groen = kanaal 3 → 25–48, oranje = kanaal 4 → 49–64, kanaal 4 noot 23 = carts stoppen). Nagebootst met `/api/hardware/devices/1/simulate`: knop 3 startte de Sweeper. Met het echte touchscreen pas na het instellen in iMAP. |
| — | 15-11-2026 | 1.2.0-rc.1 | ✅ werkt | REA schrijft zijn eigen iCON-indeling via `MIDIOUT4 (iCON V1-M)` (knop *Indeling naar het paneel schrijven* bij het apparaat, of `POST /api/hardware/devices/1/icon`). De blauwe knoppen tonen de cartnamen en blauw 1 start cart 1; een oranje cartknop en de laatste oranje knop (carts stoppen) werken. Na het opslaan blijft de indeling staan als de USB-kabel eruit en er weer in gaat. Het jogwiel bladert door de log (op het display boven de faders, log 1 t/m 8 in beide richtingen), maar het venster onder de muis scrollt nog steeds mee: het HID-muiswiel staat niet in de toewijzing die REA schrijft. |
| — | 15-11-2026 | 1.2.0-rc.1 | ✅ werkt | DJ-indeling op de iCON: gaat de DJ-tafel op de zender, dan wisselt het paneel vanzelf naar Deck A–D, en FLIP wisselt heen en terug. Faderstart werkt (fader open = deck A start, dicht = pauze). Start je deck A op het scherm met de fader dicht, dan gaat de motorfader open. SOLO = voorbeluisteren en REC = cue werken. SELECT = sync komt aan, maar mislukte omdat er geen ander deck met een tempo speelde; de reden staat nu in het logboek. Na de test aangepast, nog niet op het paneel nagetest: faderstart pas vanaf 10 % (een tikje startte het deck), en een keuze met FLIP blijft staan als de tafel van de zender gaat. Let op: met de tafel alleen op PFL (niet op de zender) volgt het paneel niet. |
| — | 15-11-2026 | 1.2.0-rc.1 | ✅ | Microfoon mono: links en rechts gemeten gelijk na *Mono uit links*, wisselt zonder herstart. |
| — | 15-11-2026 | 1.2.0-rc.1 | ✅ opgelost | Engine bleef geregeld hangen na "Application is shutting down..." bij Ctrl+C (vier van de vijf keer, ongeveer 30 s). Oorzaak: een verbonden studio (WebSocket `/ws`, of `/events` in de browser) hield de webserver open, en die wacht op open verbindingen voordat de diensten stoppen. Nu sluiten beide verbindingen zelf zodra de engine stopt. Daarna twee keer gemeten met de studio verbonden, de Behringer en de iCON: gestopt in 0,9 s en 0,4 s. |
