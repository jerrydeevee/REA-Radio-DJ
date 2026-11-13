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
| | | | | | | | |

## Uitkomsten

| Test | Datum | Versie | Uitkomst | Bevinding |
|---|---|---|---|---|
| | | | | |
