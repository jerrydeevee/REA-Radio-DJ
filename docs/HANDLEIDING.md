# Handleiding REA-Radio-DJ

**Nederlands** · [English](MANUAL.en.md)

REA-Radio-DJ bestaat uit twee delen:

- **De engine** speelt de uitzending. Hij draait als apart programma op de studio-pc en blijft spelen als de studio
  dicht is, opnieuw start of vastloopt. Een kleine bewaking start de engine zelf weer op na een crash.
- **De studio** is het scherm waarmee je de engine bedient: programmalog, spelers, bibliotheek, mixer, DJ-tafel en
  de rest. Er kunnen meerdere studio's tegelijk met één engine werken, ook op een andere pc.

Deze handleiding heeft twee delen:

- **[Deel 1 — In de studio](#deel-1--in-de-studio)** is voor wie presenteert: uitzenden, nummers kiezen, praten,
  bellers, de DJ-tafel.
- **[Deel 2 — Beheer](#deel-2--beheer)** is voor de technicus en de beheerder: installeren, instellen, plannen,
  hardware en bedrijfszekerheid.

> REA-Radio-DJ is in ontwikkeling. Onderdelen die nog niet met echte apparatuur of bij de aanbieder zijn getest,
> staan in [WERKLIJST.md](WERKLIJST.md) en [TESTPLAN.md](TESTPLAN.md).

De studio kan in het Nederlands, Engels, Duits of Spaans: **Instellingen › Uiterlijk › Taal**.

---

# Deel 1 — In de studio

## 1. Beginnen

### De studio openen

Start **REA-Radio-DJ Studio** uit het Startmenu. De studio verbindt met de engine op deze pc. Draait de engine
niet, kies dan **Studio › Engine starten**.

De eerste keer opent de **inrichthulp**: stationsnaam, kleurstijl, muziekmap en geluidskaarten. Die staat later
onder **Studio › Inrichthulp…**.

### Aanmelden

Als de beheerder gebruikers heeft aangemaakt, meld je je aan met je **naam en pincode**: klik op de naam
rechtsboven, of kies **Studio › Aanmelden…**. Zo staat in de as-run wie wat heeft uitgezonden.

- **Studio vergrendelen** (Ctrl+L): de uitzending loopt door, maar de studio gaat dicht tot iemand een pincode
  invult. Handig als je even wegloopt.
- **Afmelden**: onder **Studio**.
- Te vaak een verkeerde pincode: je naam is dan een tijdje geblokkeerd. Een beheerder kan dat meteen opheffen.

### Het scherm

Bovenaan staan de **werkruimtes**. Elke werkruimte onthoudt zijn eigen indeling.

| Werkruimte | Waarvoor |
|---|---|
| **Live** | Presenteren: aan-de-luchtbalk, bibliotheek, decks, programmalog, cartwall en mixer |
| **DJ** | Met de hand mixen op de DJ-tafel |
| **Automatisering** | Rustig meekijken terwijl de log vanzelf loopt |
| **Productie** | Bibliotheek, cue-editor, voicetracking, stemmen en AI-presentator |
| **Planning** | Klokken en dagplanning, reclame, nieuws, log-import en databronnen |
| **Rapportage** | As-run, muziekrapport, stream, hardware en bediening |

Werken met vensters:

- Elk venster versleep je aan zijn tabblad: naast een ander, eronder, of als tabblad erachter.
- **Zwevend** (menu) opent een venster los, ook op een tweede scherm.
- **Werkbladen** (menu) maakt een extra studiovenster, bijvoorbeeld voor een tweede of derde scherm.
- **Studio › Ga naar venster…**: typ een deel van de naam (log, cart, bibliotheek, dj) en druk Enter.
- **Indeling** (menu): een indeling opslaan, laden of terugzetten. **Venster plaatsen…** zet een venster precies
  waar je het wilt, ook als het zoek is.

## 2. De aan-de-luchtbalk

De balk bovenin laat altijd zien wat er gebeurt:

- **ON AIR**: wat nu speelt, met **Intro** (tijd tot de zang begint: zo lang kun je praten), **Mix over** en
  **RESTEREND**.
- **ZEG**: de presentatortekst bij dit item (bijvoorbeeld de sponsorzin bij een reclame), groot in beeld.
- **OVERLAY**: een overlay die over de muziek speelt, met een stopknop.
- Een **niveaumeter** van het programma: oranje vanaf −9 dBFS, rood vanaf −3.

De knoppen:

| Knop | Toets | Wat het doet |
|---|---|---|
| **Volgende** | F5 | Start het volgende item van de log |
| **Auto / Semi / Hand** | F6 | De speelwijze (zie hieronder); F6 zet de automatisering aan of uit |
| **Overslaan** | F7 | Slaat het item over dat ON AIR is |
| **Mic** | F8 | Microfoon open of dicht; de muziek gaat zachter zolang hij open is |
| **Stop** | F9 | Houd de knop 1 seconde vast om de automatisering te stoppen; F9 stopt direct |

### De drie speelwijzen

- **AUTO**: de programmalog loopt helemaal vanzelf.
- **SEMI**: de log loopt vanzelf; jij valt in met jingles, eigen nummers, de microfoon of een AI-break. Speelt jouw
  eigen nummer op een speler, dan wacht de log tot dat klaar is.
- **HAND**: jij start elk item, met Volgende, PLAY of een fader die opengaat.

## 3. De programmalog

De programmalog is wat er gaat spelen, van boven naar beneden. Het item ON AIR staat bovenaan.

**Iets toevoegen:**

- Uit de bibliotheek: **Enter** = als volgende, **Ctrl+Enter** = achteraan. Of sleep het nummer naar de plek in de log.
- Uit de Verkenner (een bestand op schijf): **Als volgende** of **Achteraan**. Zulke losse bestanden komen in de
  categorie *Losse bestanden*, die niet in de rotatie meedoet.
- Een **AI-break**: Ctrl+B, of **Studio › AI-break als volgende**.

**Per regel:**

| Knop | Wat het doet |
|---|---|
| ⤒ | Als volgende |
| ↑ ↓ | Eén plaats omhoog of omlaag |
| ⧉ | Kopie direct hierna |
| ⊞ | Een container openklappen: de items komen als losse regels op deze plek |
| ⇄ | De overgang naar dit item bewerken (zie [Overgangen](#overgangen-bewerken)) |
| ✕ | Uit de log; bij een container wordt het hele blok overgeslagen |

**Menu ⋯:**

- **Opnieuw plannen** gooit de klokitems weg en plant opnieuw. Wat je zelf hebt toegevoegd blijft staan.
- **Log afdrukken (PDF)…** of **Log als CSV…**: de komende log met de verwachte begintijden.

Een **container** is een blok (bijvoorbeeld een reclameblok) dat als één regel in de log staat.

### Overgangen bewerken

Klik ⇄ bij een item, of open het venster **Overgangen**. Boven zie je het uitgaande item, onder het volgende.

- Sleep het onderste item opzij om te kiezen waar het start.
- Sleep de punten van de gele volumelijnen; **dubbelklik** voegt een punt toe, **rechtsklik** haalt hem weg.
- In de middelste baan kun je een jingle of sweeper over de overgang leggen.
- **Uitfaden**: 0 (doorspelen tot het einde), 1, 3 of 6 seconden.
- **▶ Beluisteren** speelt de overgang op de PFL. **Opslaan** zet hem op het item; **Eigen weg** haalt hem weer weg.

Een eigen overgang geldt alleen zolang deze twee items na elkaar staan.

## 4. De bibliotheek

### Zoeken

Begin gewoon te typen. Hoofdletters, accenten en kleine tikfouten maken niet uit. Druk **?** voor alle
zoekmogelijkheden.

Filters kun je met woorden combineren, bijvoorbeeld: `is:mag jaar:198 duur:<4:00 queen`

| Filter | Voorbeeld | Betekenis |
|---|---|---|
| `bpm:` | `bpm:120-128` | tempo |
| `jaar:` | `jaar:198` | jaar (hier de jaren tachtig) |
| `duur:` | `duur:<3:30` | lengte |
| `energie:` | `energie:4-5` | je eigen inschatting van de energie |
| `is:mag` | | alleen wat nu ingepland mag worden |

- **Filters ▾**: categorie, decennium, sortering en snelfilters.
- **Bewaar…**: bewaart de zoekopdracht als lijst. Een bewaarde lijst toont altijd de stand van nu.
- **Velden ▾**: kies welke gegevens onder de titel staan. **Grote regels** is voor een aanraakscherm.

### Sneltoetsen in de lijst

| Toets | Wat het doet |
|---|---|
| Enter | Als volgende in de log |
| Ctrl+Enter | Achteraan in de log |
| Spatie | Voorbeluisteren op de PFL |
| Esc | PFL stoppen, of het zoekvak leegmaken |
| Ctrl / Shift | Meer nummers kiezen |
| Ctrl+Shift+C | De selectie op het klembord |
| Ctrl+Z | De laatste wijziging in de bibliotheek terugzetten |

### Rechtsklik op een nummer

Als volgende of achteraan in de log, in standby 1 of 2, PFL, als overlay, **Cues bewerken**, **Aan / uit** (niet
meer inplannen), muziek ↔ jingle omzetten, en **Stems maken** voor de DJ-decks.

Staat er een teken bij een nummer, wijs het dan aan: het vertelt **waarom dit nummer nu niet ingepland wordt**
(bijvoorbeeld dezelfde artiest te kort geleden).

### Het detailpaneel

Klik **Details** voor het paneel naast de lijst. Daar staan:

- de golfvorm (klik om vanaf die plek voor te luisteren);
- **Aandacht nodig**: bijvoorbeeld te zacht, clipping of een lage bitrate;
- de tags (artiest, titel, album, genre, jaar, taal, stemming, waardering, energie) met **Tags opslaan**;
- **Presentatortekst**: wat je bij dit item zegt. Die staat groot in de aan-de-luchtbalk zolang het item speelt;
- de hoes (**Hoes kiezen…**, **Hoes weghalen**) en hoe vaak en wanneer het nummer is uitgezonden.

## 5. Spelers

### Live-decks

In de werkruimte Live speelt de log op deck A en B:

- **⏭ Volgende**: start B nu en mixt over.
- **Snel uit**: A snel uitfaden en door naar B.
- **■**: de automatisering stoppen, met een fade.
- **Uit de log**: haalt dit item uit de log.

Een deck dat ON AIR is, heeft een signaalbalk. Blijft die dim, dan zie je **geen geluid**.

### Standby-spelers

Spelers voor je eigen nummers naast de log, met een **stapel**:

- **Laad selectie** laadt het gekozen bibliotheeknummer.
- **+ stapel** zet de selectie achter het huidige item.
- **Start / pauze**, **Stop** (terug naar het begin), **Uitwerpen**.
- Klik in de golfvorm om te zoeken; scroll om in en uit te zoomen.

Shift+F5 en Shift+F6 starten of pauzeren speler 1 en 2.

### PFL (voorbeluisteren)

De PFL speelt alleen in de koptelefoon, nooit op de zender. Spatie in de bibliotheek start hem, Esc stopt hem.
Ctrl+P = afspelen of pauze.

### Cartwalls

Twee cartwalls (Cartwall 1 en 2) van 4 pagina's met 16 knoppen.

- Sleep een nummer of jingle op een knop, of kies via rechtsklik **Selectie uit bibliotheek toewijzen**.
- Rechtsklik heeft ook: **Herhalen (lus)**, **Overlay aan/uit** (de automatisering wordt zachter), **Kleur**,
  **Cue-editor** en **Leegmaken**.
- F10 stopt alle carts.

### Overlays

Een overlay speelt bovenop de automatisering, en de muziek wordt zachter zolang hij loopt. Kies in de bibliotheek
**Overlay**, of in het venster Overlays **Speel selectie als overlay**. Shift+F9 stopt alle overlays.

### Klembord

Een parkeerplek voor nummers. Sleep nummers erheen uit de bibliotheek, de log, een deck of een speler, of zet de
selectie erop met Ctrl+Shift+C. Later sleep je ze weer naar de log, een speler, een deck of de cartwall.

Met **Als container in de log** zet je alles op het klembord als één regel in de log, als volgende of achteraan.

## 6. Praten

- **Mic** (F8) zet de microfoon open. De muziek gaat dan zachter (de beheerder stelt in hoeveel).
- De **Intro**-teller in de aan-de-luchtbalk telt af tot de zang begint: zo lang kun je over de intro praten.
- Wil je iets letterlijk zeggen bij een item, zet het dan als **presentatortekst** in de bibliotheek (detailpaneel).

## 7. Voicetracking

Met voicetracking neem je je praatje vooraf op, precies tussen twee nummers. Open het venster **Voicetracking**
(werkruimte Productie):

1. Kies de **plek in de log**: tussen welke twee items je praat.
2. Stel de **voorloop** in: hoeveel seconden van het einde van het vorige item je hoort.
3. **START**: het einde van het vorige item speelt op de PFL en de opname begint.
4. **VOLGENDE**: het volgende item start nu. Je praat dan over de intro.
5. **STOP**: de opname is klaar.
6. **▶ Beluisteren (PFL)**, en daarna **In de log zetten**, **Opnieuw opnemen** of **Weggooien**.

In de uitzending begint je stem waar je begon te praten, en de muziek gaat dan zachter. Het volgende item start op
het moment dat je VOLGENDE drukte. De twee items rond de voicetrack blijven staan als de log opnieuw gepland wordt.
Je hoort alles op de PFL; er gaat niets naar de uitzending.

**Vanaf huis:** open in de browser `https://<engine>:8443/voicetrack/` (of via 🎙 op de remote-pagina), meld je
aan, en neem op met de microfoon van je laptop of telefoon. De opname komt op dezelfde manier in de log.

## 8. Bellers en verslaggevers

Het venster **Verbindingen** heeft twee VoIP-kanalen. De overkant hoort de uitzending, maar niet zichzelf.

**Iemand bellen:**

1. Kies bij een kanaal een **station** uit het adresboek en klik **Gebruik**, of typ een nummer en klik **Bel**.
2. Luister eerst met **PFL**.
3. Klik **Op de zender** als de beller de lucht op mag. In de mixer is dat de fader van VoIP 1 of 2.
4. **Ophangen** als je klaar bent.

**Gebeld worden:**

Een inkomend gesprek gaat over op het eerste vrije kanaal. Kies **Opnemen** of **Weigeren**.

**Vaste lijn** (bijvoorbeeld een verslaggever met een codec of een andere studio): kies het station met de vaste
lijn en klik **Gebruik**, of vul poort, adres en codec in en klik **Vaste lijn openen**.

**Opnemen:**

- **REC** neemt op wat er binnenkomt, als WAV in `Opnamen\Gesprekken`.
- Met **Gesprekken opnemen** aan gebeurt dat bij elk gesprek vanzelf.

Een waarschuwing bij een kanaal betekent: geen audio, meer dan 2 % verlies, of een buffer die vaak leegloopt.

## 9. De DJ-tafel

Werkruimte **DJ**: decks met golfvormen en beatstrepen, een mengpaneel en een crossfader.

### Op de lucht en terug

- **Alleen PFL**: de DJ-tafel gaat niet de zender op en de automatisering is hoorbaar. Zo oefen of bereid je voor.
- **DJ ON AIR**: wat je mixt gaat de zender op. De automatisering loopt onhoorbaar mee.
- **↩ Teruggeven aan automatisering**: de tafel gaat van de lucht en de automatisering is meteen hoorbaar, op de
  plek waar hij nu is.
- **Volg de zender op deck A**: deck A speelt mee met wat de automatisering uitzendt, zodat je naadloos kunt
  overnemen. Deck A moet daarvoor leeg zijn.
- **2 decks / 4 decks**: met vier decks worden de decks compacter.

### Een deck

| Onderdeel | Wat het doet |
|---|---|
| **Cue** | Spelend: terug naar het cuepunt en wachten. Stil: het cuepunt komt op deze plek |
| **Start** | Start of pauze |
| **Sync** | Neemt het tempo over van het andere spelende deck en legt de beats gelijk |
| **− / +** | Even afremmen of aanduwen om de beats gelijk te leggen |
| **Tempofader** | Omlaag is sneller; het bereik is 8, 12 of 50 %. **0 %** zet het eigen tempo terug |
| **Toon vast** | Sneller of langzamer zonder dat de toonhoogte verandert |
| **HOTCUES** | Leeg: onthoudt deze plek. Gevuld: springt erheen. Rechtsklik: wissen |
| **LOOP** | **In** en **Uit** maken een loop; ÷2 en ×2 halveren of verdubbelen; een getal = een loop van zoveel beats vanaf hier |
| **ROLL** | Ingedrukt houden: een korte loop. Bij loslaten loopt het nummer verder alsof het gewoon doorspeelde |
| **EFFECT** | Echo, Flanger of Galm op de tel; ◀ ▶ voor de lengte, de draaiknop voor de mix, **FX AAN** |
| **STOP** | **Rem** (uitlopen tot stilstand) of **Backspin**; daarna pauzeert het deck |
| **Censuur** | Vasthouden om een woord te maskeren: het nummer klinkt zolang achteruit en loopt daarna verder waar het gebleven zou zijn |
| **STEMS** | **Zang**, **Drums**, **Bas** en **Rest** los aan of uit |

Een nummer laden: sleep het uit de bibliotheek of van het klembord op een deck.

**Stems:** als je een nummer op een deck laadt, worden de stems op de achtergrond gemaakt. Dat duurt ongeveer een
derde van de lengte van het nummer. Naast de knoppen staat hoe ver het is. Daarna zet je bijvoorbeeld de zang uit
voor een instrumentale versie. Vooraf kan het ook: in de bibliotheek, rechtsklik › **Stems maken**.

### Het mengpaneel

- Per deck: **Gain**, **Filter** (links laat alleen het laag over, rechts alleen het hoog), **Hoog**, **Midden** en
  **Laag** met **Kill**, de kanaalfader en **PFL** (het deck in de koptelefoon, ook met de fader dicht).
- Een draaiknop bedien je met slepen of het muiswiel; dubbelklik zet hem in het midden.
- **EQ 0** zet equalizer en filter terug.
- **CROSSFADER**: links de decks A en C, rechts B en D. **Scherp** laat beide kanten open tot vlak voor het einde.
- **KOPTELEFOON · CUE ↔ MASTER**: links alleen de decks met PFL, rechts alleen de mix van de tafel.
- **Quantize**: hot cues, loops en rolls vallen op de tel.
- **Begrenzer**: de mix komt nooit boven −1 dBFS, ook niet in de opname.
- **● Mix opnemen**: neemt op wat de tafel mixt, op en van de lucht. Na het stoppen staat de mix in de bibliotheek
  bij Opnamen.

### Automix en sampler

- **Automix (DJ)**: sleep nummers in de lijst en zet **AUTOMIX** aan. De lijst speelt zichzelf op deck A en B (of
  alle vier).
  - Overgangen: **Beatmix** (tempo en tel gelijk), **Fade**, **Cut** (op de tel), **Echo** en **Filter**; ◀ ▶ maakt
    de overgang korter of langer.
  - **Nu mixen** begint de overgang meteen.
  - **Kanaalfaders**: automix gebruikt de kanaalfaders in plaats van de crossfader, zodat de motorfaders van een
    bedieningspaneel meebewegen.
- **Sampler (DJ)**: een cartwall die via de DJ-tafel speelt. Off air hoor je hem in de koptelefoon, on air op de
  zender. Met Quantize start een pad op de volgende tel.

## 10. De AI-presentator

- **AI-break als volgende** (Ctrl+B) zet een praatje van de AI-presentator in de log. Het wordt vooraf geschreven
  en ingesproken; de uitzending wacht er nooit op.
- Met **Script eerst goedkeuren** aan kijk je elk script na: **✓ Goedkeuren en inspreken** of **↻ Opnieuw
  schrijven**. Is het niet op tijd goedgekeurd, dan komt er een jingle in de plaats.
- **Berichten om voor te lezen** (venster AI-presentator): bijvoorbeeld een felicitatie, met een geldigheid en een
  aantal keer.
- **Taal op de zender**: naast de naam van de presentator kies je welke taal hij op de zender spreekt (Nederlands,
  Engels, Duits of Spaans). Het taalmodel schrijft in die taal, de sjablonen en de woorden rond nieuws en weer volgen
  hem, en als het stemprofiel geen stem noemt wordt een Windows-stem in die taal gekozen. Kies een stem die de taal
  spreekt.

## 11. Uit de uitzending knippen

Het venster **Uit de uitzending knippen** zet een stuk uit de uitzendopname als item in de bibliotheek, bijvoorbeeld
het interview van net. Kies **Laatste 5 min**, **10 min**, **30 min** of **Uur**, of vul een begin- en eindtijd in.
Geef een titel en klik **Knippen naar de bibliotheek**. Het stuk komt bij de jingles in de categorie Opnamen en mag
over het hele uur heen lopen.

Dit werkt alleen als de uitzending wordt opgenomen (Instellingen › Opnemen).

## 12. Klok, berichten en alarmen

- Het venster **Tijd** toont de studioklok en tellers: ON AIR, INTRO, VOLGENDE, KLOK en VASTE TIJD. Met ⚙ kies je
  de klokstijl, de grootte en de flitskleur aan het einde van een item.
- Het venster **Berichten** toont meldingen en alarmen. Let vooral op:
  - **Stilte op de zender**: het is te lang stil. Afhankelijk van de instelling start de engine zelf de
    automatisering of een noodcategorie. Kijk of er iets hapert.
  - **Volume van de geluidskaart veranderd**: Windows of een ander programma heeft een kaart zachter gezet of gedempt.
  - **Nieuws overgeslagen**: er was geen actueel nieuws, dus het hele blok is overgeslagen (er klinkt dan ook geen
    opener).

## 13. Op afstand

Op een telefoon of tablet in het netwerk open je `http://<engine>:8600/remote/` (of via https op poort 8443):

- **Bewaken**: wat speelt, wat komt, de niveaumeter en de laatste meldingen.
- **Bedienen**: start volgende, overslaan, automatisch of live-assist, microfoon, carts stoppen.
- **Carts**: de cartwalls als grote knoppen.

Meekijken kan altijd. Voor een handeling vraagt de pagina je naam en pincode, of het bedieningswachtwoord. De pagina
volgt de taal van de browser; `?lang=en` (of `nl`, `de`, `es`) kiest een andere.

## 14. Sneltoetsen

| Toets | Wat het doet |
|---|---|
| F5 | Volgende |
| F6 | Automatisering aan of uit |
| F7 | Overslaan |
| F8 | Microfoon open of dicht |
| F9 | Stop |
| F10 | Alle carts stoppen |
| Shift+F5 / Shift+F6 | Speler 1 / 2 start of pauze |
| Shift+F9 | Alle overlays stoppen |
| Ctrl+B | AI-break als volgende |
| Ctrl+P | PFL afspelen of pauze |
| Ctrl+L | Studio vergrendelen |

De beheerder kan dit veranderen. Het overzicht staat in **Studio › Bediening en sneltoetsen…**.

---

# Deel 2 — Beheer

## 1. Installeren

### Met de setup

Download `REA-Radio-DJ-<versie>-setup.exe` bij de uitgave op GitHub. .NET hoeft niet apart geïnstalleerd te worden.

- De setup installeert voor de eigen gebruiker, zonder beheerdersrechten, in `%LocalAppData%\Programs\REA-Radio-DJ`.
- De engine bewaart database, muziek en opnamen naast zichzelf, in `engine\data` en `engine\media`.
- Een snelkoppeling op het bureaublad en het starten van de engine bij aanmelden zijn keuzes in de setup.
- Een nieuwere setup installeert over de oude heen. Bij verwijderen (Windows-instellingen › Apps) blijven
  `engine\data` en `engine\media` staan.
- Een nieuwere uitgave verschijnt als knop rechtsonder in de studio. Er wordt niets vanzelf gedownload.

### De eerste start

De **inrichthulp** zet in vier stappen het belangrijkste klaar:

1. de naam van het station;
2. de kleurstijl van de studio;
3. de map met muziek en de map met jingles (elke submap wordt een categorie);
4. de geluidskaart voor de uitzending en die voor de PFL.

Kies voor de PFL een **andere kaart** dan voor de uitzending, anders hoort de luisteraar je voorbeluisteren.

### Waar wat staat

| Wat | Waar |
|---|---|
| Database, instellingen, klokken, planning | `engine\data` (de datamap) |
| Muziek en jingles (standaard) | `engine\media\music` en `engine\media\jingles` |
| Uitzendopnamen, gespreksopnamen | de opnamenmap (Instellingen › Opnemen) |
| Stems voor de DJ-decks | `engine\data\stems` (hoogstens 20 GB) |
| Scheidingsmodel voor stems | `engine\data\models\htdemucs.onnx` |
| Basisinstellingen van de engine | `appsettings.json` naast de engine |

Instellingen die je in de studio maakt, bewaart de engine in zijn eigen database. Die gaan voor op
`appsettings.json`.

Het icoon van de engine bij de klok heeft een menu in de taal van Windows; zet `ReaRadioDj:Language` (`nl`, `en`, `de`,
`es`) in `appsettings.json` voor een andere.

## 2. De engine draaiende houden

- **Instellingen › Systeem › Engine met bewaking starten als ik me aanmeld op deze pc**: na een herstart van de pc
  speelt het station dan zonder dat iemand iets doet.
- **Engine opnieuw starten** (Systeem): de bewaking start hem meteen weer; wat ON AIR is, gaat verder.
- De bewaking start de engine ook opnieuw na een crash of vastloper, en hij gaat verder waar de log was.
- Onder **Laatste meldingen van de bewaking** zie je wanneer dat gebeurde.

## 3. Instellingen

**Studio › Instellingen…** Na het wijzigen klik je **Opslaan**. **Opnieuw lezen** gooit je wijzigingen weg. Met
gebruikers ingeschakeld is dit venster alleen voor beheerders.

### Station

- Naam van het station, en de mappen met muziek en jingles. Gebruik het pad zoals de engine-pc het ziet. Na het
  wijzigen wordt de bibliotheek opnieuw ingelezen.
- **Speelwijze als de engine start**: AUTO, SEMI of HAND.
- **Programmalog onder de faders**: muziek op speler A/B, jingles op de cartwall, AI-breaks op de AI-presentator,
  elk met een eigen fader. Uit: alles speelt op het automatiseringskanaal.

### Geluid

- **Geluidskaarten** voor de uitzending en de PFL, met een testtoon. Let op: de testtoon voor de uitzending gaat ook
  de zender en de stream op.
- **Begrenzer en loudness op de zender**: de begrenzer houdt pieken onder het plafond. De loudness (EBU R128) staat
  bovenin naast de meter.
- **ASIO, samplerate en buffer**: ASIO gaat rechtstreeks naar het stuurprogramma van een studiokaart. Dit wordt
  actief na een herstart van de engine. Lukt ASIO niet, dan valt de engine terug op de Windows-kaart en meldt dat.
  Neem de samplerate van de kaart en de mengtafel. Hapert het geluid, maak de buffer dan groter.
- **Microfoon**: de ingang en het kanaal (mono als de microfoon op één ingang van een paar zit), en hoeveel de
  muziek zachter gaat onder de microfoon en onder een overlay.
- **Werkwijze**:
  - *Software*: de engine mengt.
  - *Mengtafel*: elk kanaal met een eigen uitgang gaat op vol naar de tafel. Fader, dempen en het ducken onder de
    microfoon gelden dan niet, want dat doet de tafel.
- **Uitgang per mengkanaal**: geef een kanaal een eigen uitgang (1/2, 3/4, 5/6 of 7/8 van een kaart met meer
  kanalen). Zo'n kanaal zit dan niet meer in de uitzending, de stream en de stiltebewaking.

### Mixen

- **Loudness van de zender**: −16 LUFS is gebruikelijk voor een stream, −23 voor omroep.
- **Automatische cuepunten**: de drempels waarmee de engine begin, mixpunt en einde zoekt. Cuepunten die je met de
  hand zette blijven staan.
- **Overgangen**: de langste overlap, de fade bij Volgende of Overslaan, en de muziek onder een stem.
- **Zachte start op een vaste tijd**: een zacht nieuws- of reclameblok wacht op het einde van het item, maar
  hoogstens zo lang. Daarna start het blok meteen en faadt het item in 2 seconden uit.
- **Overgangen per soort en categorie**: bijvoorbeeld van jingle naar nummer zonder overlap, of van Klassiek naar
  alles met 6 seconden. De bovenste regel die past telt. Bewaar ze met **Regels opslaan**.
- **Speelduur per soort en categorie**: bijvoorbeeld jingles hoogstens 30 seconden, of reclame korter dan 60
  seconden speelt altijd helemaal uit. Bewaar met **Speelduur opslaan**.

### Opnemen

- **Uitzending opnemen**: één MP3 per klokuur, met de microfoon erin.
- **Microfoon apart opnemen**, eventueel direct in de bibliotheek (categorie Opnamen).
- **Alleen het praten opnemen**: als luistercontrole. Alleen de stukken met geluid terwijl de microfoon of een
  ingang open staat, met een halve seconde vóór de inzet.
- Kwaliteit, bewaartermijn en map. 128 kbit/s is ongeveer 58 MB per uur en ruim 500 GB per jaar; 64 kbit/s is de
  helft. De bewaartermijn geldt ook voor de gespreksopnamen.
- Een los kanaal opnemen kan met **REC** in de mixer.

### Bewaking

- **Stilte op de zender**: drempel, hoe lang, en wat er dan gebeurt: melden, de automatisering starten, of iets uit
  een **noodcategorie** spelen. Kies een duur die langer is dan de langste stilte in je eigen programma. De eerste
  halve minuut na het starten telt niet mee.
- **Volume van de geluidskaart**: de engine kijkt elke twee seconden of Windows of een ander programma een kaart
  zachter zette of dempte. Met **Een veranderde kaart meteen terugzetten** zet hij hem zelf terug. Heb je bewust iets
  veranderd, klik dan **Huidige stand is normaal**. ASIO telt niet mee.

### Rotatie

Hoe de automatisering nummers kiest: dezelfde artiest pas weer na zoveel nummers, hetzelfde nummer pas weer na
zoveel nummers, en hoeveel items de log vooruit plant. De klokken en regels zelf staan in de Planner.

### Stream

De Icecast-server, het bronwachtwoord, de bitrate, genre en website, en **Stream starten zodra de engine start**.
Een lopende stream neemt nieuwe gegevens pas over nadat je hem in het venster Stream stopt en weer start. Extra
servers stel je in het venster Stream in (zie [Stream en titels](#11-stream-en-titels)).

### Spotify en AcoustID

- **Spotify** gebruikt alleen gegevens (wensenlijst, album, jaar, ISRC, hoes), nooit geluid. Maak een app in het
  Spotify-dashboard (Web API aanvinken) en vul Client ID en Client Secret in.
- **AcoustID** herkent nummers aan het geluid, ook als de tags ontbreken. Registreer een applicatie op acoustid.org
  en vul de API-sleutel in. Alleen de vingerafdruk gaat naar AcoustID, nooit het geluid. Daarvoor is `fpcalc` nodig
  (`scripts\fetch-fpcalc.ps1`).

### Op afstand

- **De engine is op het netwerk te bereiken**: voor een studio op een andere pc, de browserstudio en de remote-app.
  Dit werkt na een herstart van de engine. Zet dan ook de poorten open in de firewall (zie
  [Poorten](#poorten-en-firewall)).
- **Https** gebruikt een eigen certificaat van het station, op poort 8443. Installeer het één keer op een telefoon:
  - **Android**: Instellingen › Beveiliging › Meer › Versleuteling en inloggegevens › Certificaat installeren ›
    CA-certificaat.
  - **iPhone**: open het adres in Safari, en zet het aan bij Instellingen › Algemeen › Info › Certificaatvertrouwen.
- **Bediening op afstand**: het adres en de TCP- en OSC-poort voor de commandobus, en het **wachtwoord**. Zet een
  wachtwoord als je op het hele netwerk luistert. Zonder wachtwoord en zonder gebruikers kan een andere computer
  alleen meekijken.
- **Afstandsbedieners met eigen rechten**: een tablet of knoppenkast die maar een deel mag, bijvoorbeeld alleen de
  cartwall. Hij krijgt een eigen sleutel of wordt herkend aan zijn vaste adres.

### Uiterlijk en Deze studio

- **Uiterlijk** geldt alleen voor deze computer:
  - de **taal** van de studio (Nederlands, Engels, Duits of Spaans);
  - stijl (Studio donker, Nacht, Licht, Hoog contrast), accentkleur, hoeken, knoppen en golfvorm;
  - **Grootte van alles** en **Aanraakmodus**, voor een touchscreen of een scherm op afstand;
  - **Rustige kleuren**, en de kleuren per soort item.
- **Deze studio**: het adres van de engine waarmee deze studio verbindt, en of de studio de engine start als die nog
  niet draait.

De tabbladen **Systeem**, **Reservestudio**, **Centrale bibliotheek** en **Gebruikers** staan verderop bij
[Bedrijfszekerheid](#13-bedrijfszekerheid), [Meerdere studio's](#14-meerdere-studios) en
[Gebruikers](#12-gebruikers-en-rechten).

## 4. De bibliotheek beheren

### Mappen en shares

In de bibliotheek: **Beheer** › **Mappen en shares**.

- **Map toevoegen**: een map op deze pc, een andere schijf of een share (`\\server\muziek`). Kies of het muziek of
  jingles zijn, en eventueel een vaste categorie. Leeg betekent: elke submap wordt een categorie.
- **Bewaken**: nieuwe en gewijzigde bestanden worden direct verwerkt. Verplaatste bestanden worden teruggevonden,
  met hun cues en geschiedenis.
- Bij het inlezen meet de engine BPM en beatgrid, toonsoort, loudness, het verloop en de intro- en outromarkers, en
  hij controleert de kwaliteit.
- Een map verwijderen bewaart de nummers met al hun gegevens. Komt een nummer later terug, dan wordt het herkend.
- Een **niet bereikbare** map (share weg) houdt zijn nummers; ze komen terug zodra de map er weer is.

**Vervangende locaties**: staat een bestand niet op zijn pad (share weg, andere stationsletter, andere pc), dan
zoekt de engine hetzelfde bestand onder de vervangende map, bijvoorbeeld `Y:\Muziek → \\nas\muziek`.
**Definitief omzetten** past de paden in de bibliotheek blijvend aan.

### Categorieën

Onder **Beheer › Categorieën en item**: een categorie toevoegen (muziek of jingle), de selectie verplaatsen, of de
selectie aan- of uitzetten. Een uitgezet nummer wordt niet meer ingepland.

### Cue-editor

Open de **Cue-editor** (rechtsklik › Cues bewerken, of **Laad selectie**):

- Sleep de markers: cue-in, intro's, outro, mix, cue-out en hook. Met ⌖ zet je een marker op de cursor.
- Scrollwiel = zoom, Shift+scroll = schuiven, dubbelklik = alles.
- **▶ vanaf cursor** luistert op de PFL.
- **Automatisch** gooit je eigen punten weg en bepaalt ze opnieuw. **Ongedaan** en **Opslaan**.
- **Gebeurtenissen in het nummer**: op een plek in het nummer een tijdje zachter of harder (**+ Volume op cursor**),
  of een opdracht (**+ Opdracht op cursor**, bijvoorbeeld `CART 1 3`). Die gaat elke keer af als het nummer op de
  zender speelt.

### Tags, hoezen en gegevens aanvullen

- In het detailpaneel: **Tags opslaan**, **Markers in het bestand** (cues, beatgrid en BPM), **Alle tags in het
  bestand**, en een hoes kiezen of weghalen.
- Voor een selectie: **Tags in de bestanden** zet alles wat de bibliotheek weet in de bestanden.
- **Databronnen › Metadata**: zoekt ontbrekend album, jaar, ISRC, label, genre, componist en hoes op. Er verandert
  niets tot je **Aangevinkte toepassen** kiest, en dan worden alleen lege velden gevuld. **Automatisch aanvullen als
  een nummer voor het eerst draait** doet het op de achtergrond.
- **Databronnen › Databases**: een externe database via ODBC als bron voor de bibliotheek, met een query. **Test de
  query** en **Nu inlezen**.

### Stems

Voor de stems op de DJ-decks moet het model in `engine\data\models\htdemucs.onnx` staan. Stems worden op de
processor gemaakt, met een paar threads op lage prioriteit, zodat de uitzending er geen last van heeft. Ze nooit op
de videokaart laten draaien: dat heeft de uitzending een keer stilgelegd. Stems worden bewaard in `engine\data\stems`,
tot 20 GB. Daarna gaan de stems die het langst niet gebruikt zijn eerst weg. Een nummer van 2,5 minuut kost
ongeveer 110 MB.

## 5. Planning

Werkruimte **Planning**, venster **Planner**.

### Klokken

Een klok is een uur: een lijst van plekken die van boven naar beneden gespeeld worden. Duurt het uur langer, dan
begint de klok weer bovenaan. Elk nieuw uur begint bovenaan.

- **Nieuwe klok**, een naam en een kleur.
- **+ muziek**: een plek uit een categorie. **+ jingle**: een jingle-categorie of één vast item. **+ AI-presentator**:
  een AI-break.
- Een plek kan ook een **afspeellijst** zijn (M3U, PLS of XSPF), ook als een ander programma die lijst schrijft.
  Alleen bestanden uit de bibliotheek spelen; **Controleer** laat zien hoeveel dat er zijn.

### Weekgrid

**Schilder met klok**: kies een klok en klik de uren. Rechtsklik vult een hele rij of kolom; **Hele week** vult
alles. Na het wijzigen van klokken of het grid: **Log opnieuw plannen**.

De tabbladen **Dag** en **Week** laten zien wat er gepland staat, met de vaste tijden en streams van vandaag.

### Vaste tijden

Iets op een vaste tijd, bijvoorbeeld een tune op het hele uur:

- Uren (`*` = elk uur, of bijvoorbeeld `7-9,12,17-19`), minuut en seconde.
- Wat: een categorie of de selectie uit de bibliotheek.
- **Hard**: onderbreekt op de seconde. **Zacht**: na het lopende item. **Overlay**: eroverheen, de muziek wordt
  zachter.

Materiaal (ook een AI-break) wordt 3 minuten vooraf klaargezet. Is het te laat, dan wordt het overgeslagen en nooit
later gespeeld.

### Regels

Bijvoorbeeld: maximaal 2 op rij uit dezelfde categorie, of Hardrock mag niet direct op Klassiek volgen. Houdt een
regel een item tegen, dan wordt die plek **overgeslagen**, of **uitgesteld** (na het volgende item nog één keer
proberen). Een nieuw uur begint schoon.

### Externe streams

Een Icecast- of SHOUTcast-adres dat in een tijdvenster wordt bewaakt. Is de stream live, dan gaat hij ON AIR en neemt
hij het programma over. Stopt de stream of eindigt het venster, dan gaat het programma verder. **Live stream
stoppen** haalt een lopende stream er meteen af.

### Log-import

Venster **Log-import**, voor een log uit een externe muziekplanner:

1. Maak onder **Profielen** een profiel: waar staat elk veld (kolom, of vanaf welk teken en hoe lang), het
   tijdformaat, en zo nodig een padvervanging als de planner de muziek op een andere plek ziet.
2. Onder **Importeren**: kies profiel, datum en logbestand, dan **Controleren** en **Importeren**.
3. Onder **Uren** zie je welke uren de geïmporteerde log spelen in plaats van hun klok. Raakt een uur op, dan vult de
   klok aan.

**Bibliotheek exporteren…** geeft de planner een CSV met alle nummers en jingles.

## 6. Reclame

Venster **Reclame**:

- **Campagnes**: adverteerder, campagnenaam, van en tot, uren, aantal per dag en hoogstens per uur, en de spots (die
  wisselen elkaar af).
- **Blokken**: een reclameblok op een vaste minuut, met opener, sluiter en een grootste lengte. **Test: wat komt
  erin?** laat het zien.
- De blokken worden 3 minuten vooraf gevuld:
  - eerst de campagnes die het verst achterlopen op hun aantal per dag;
  - nooit twee spots van dezelfde adverteerder in één blok;
  - nooit langer dan het blok mag duren.

  Heeft geen enkele campagne een spot nodig, dan valt het hele blok weg, ook opener en sluiter.
- **Uitzendbewijs**: CSV per uitzending, een overzicht per adverteerder en campagne, of JSON om te koppelen.

Een reclameblok staat het best op **Zacht**: het komt dan na het lopende nummer.

## 7. Nieuws en weer

Venster **Nieuws**. Een nieuwsblok heeft uren, een minuut, een opener en een sluiter, en een van deze bronnen:

- **Een bestand** dat binnenkomt in een map (bijvoorbeeld van een nieuwsdienst), hoogstens zoveel minuten oud.
- **RSS-koppen**, letterlijk voorgelezen. Er wordt niets bij verzonnen.
- **AI-nieuws**: het taalmodel herschrijft landelijke en regionale bronnen tot een bulletin met bronvermelding.
  Noemt het iets dat niet in de bron staat (bijvoorbeeld een getal), dan worden de koppen letterlijk voorgelezen.
- **Weerbericht** voor een gekozen plaats.

Kies een stem en eventueel een **tune eronder**. Met **Proef: tekst schrijven** en **Proef met stem (PFL)** test je
een blok zonder uit te zenden. De voorbeelden *Kort regionaal nieuws (:30)*, *Nieuws op het hele uur* en
*Weerbericht* zijn een goed begin.

Een blok wordt 3 minuten vooraf klaargezet. Is er geen actueel nieuws, dan wordt het hele blok overgeslagen met een
melding in Berichten.

## 8. De AI-presentator en stemmen

### Taalmodel en persona

Venster **AI-presentator**, onder **Instellingen: taalmodel en persona**:

- **AI-presentator aan**, een naam, de **taal op de zender** ernaast, en het taalmodel. Gebruik **Ollama op deze pc** of een ander OpenAI-compatibel
  adres. Leeg betekent: vaste sjabloonteksten.
- Lengte, variatie, persona, en een **muziekbed** (een categorie met beds onder de stem).
- **Proefscript schrijven**: het taalmodel schrijft een tekst. Er wordt niets ingesproken of uitgezonden.
- **Weer, nieuws, berichten en outro**: per break noemt de presentator hoogstens één ding. Een **outro**-jingle kan
  direct na elke break komen.
- **Persona's per dagdeel**: per dagdeel een eigen presentator met naam, karakter, stem, lengte en muziekbed.
- **Creatieve presentator**: praat de plaatjes aan elkaar en snijdt af en toe een onderwerp aan. Je stelt de toon
  in (serieus, grappig, leuk, randje), en waar de onderwerpen vandaan komen: eigen onderwerpen, online
  onderwerpen van de zender, of ingebouwde onderwerpen. Niets kwetsends, geen politiek en geen verzonnen nieuws.

### Stemserver (Higgs)

Higgs TTS draait als aparte server in WSL met een NVIDIA-videokaart. Onder **Stemserver (Higgs)** staan het adres
en het startcommando, en **Stemserver automatisch starten met de engine**. Het model laden duurt een paar minuten;
tot dan spreekt de reservestem. Het startcommando kan alleen op de studio-pc zelf worden gewijzigd. De installatie
staat in de README.

> Let op: draai de stemserver liever niet op dezelfde pc als de engine als die videokaart ook het beeld of andere
> taken doet. Zware berekeningen op de videokaart kunnen de pc laten haperen.

### Stemmen

Venster **Stemmen**:

- **STEMMEN PER ROL**: welke stem de AI-presentator, de nieuwslezer en het weer gebruiken.
- **Standaardstemmen toevoegen**: vijf Higgs-stemmen met een eigen karakter, plus de Nederlandse Windows-stem als
  reserve.
- **+ Higgs-stem**, **+ Windows-stem** of **+ Online stem** (ElevenLabs of Azure; die kosten geld per teken en sturen
  de tekst naar de dienst). Elke stem kan een **reservestem** hebben.
- **Stem klonen**: een korte schone opname (4 tot 40 seconden) met de letterlijke tekst. Zonder vastgelegde
  schriftelijke toestemming wordt een gekloonde stem niet gebruikt.
- **Mijn eigen stem**: een presentator legt zelf zijn stem vast, kiest voor welke rollen die gebruikt mag worden en
  geeft toestemming. **Mijn stem verwijderen** trekt dat weer in.
- **Online stemmen en controle (Whisper)**: elke break terugluisteren en vergelijken met het script. Onder een
  minimale score wordt opnieuw ingesproken, en na mislukte pogingen komt er eventueel een jingle in de plaats.

## 9. Mixer en routing

Venster **Mixer en routing**. Elk kanaal heeft een fader, **M** (dempen), **REC** (opnemen naar WAV), **ZENDER**
(naar de uitzending) en ⚙ voor de **kanaalstrip**:

- gain, balans, equalizer (laag, midden, hoog) en compressor;
- automatische niveauregeling;
- een effect;
- VST-effecten (VST 2, 64-bit). Gebruik eerst **Plug-ins zoeken**.

**Ingangen** (lijn-in, een tweede microfoon, een testtoon) komen als kanaal in de mixer en staan eerst dicht. Met
**ZENDER** zet je ze door.

Het venster **Uitsturing** toont het programma groot: piek, loudness (kort en geïntegreerd), true peak en de
begrenzer. Per kanaal staat er een meter met OPEN, DICHT of MIC OPEN, leesbaar vanaf de andere kant van de studio.

## 10. Verbindingen (VoIP) instellen

### SIP-account

In het venster Verbindingen, **SIP-account**:

- Gebruiker en nummer, inlognaam en wachtwoord. Het wachtwoord wordt versleuteld opgeslagen en nooit teruggestuurd.
- Registrar, domein, proxy, outbound proxy en STUN-server.
- **Codecs** op volgorde van voorkeur. De eerste die de overkant ook kent, wordt gebruikt:

  | Codec | Kwaliteit | Gebruik |
  |---|---|---|
  | `opus` | 48 kHz, studiokwaliteit over internet | LUCI Live, Comrex en andere codecs |
  | `g722` | 16 kHz breedband | HD-telefonie |
  | `pcma` / `pcmu` | G.711, 8 kHz | gewone telefoon; `pcma` is gangbaar in Europa |
  | `l16` | ongecomprimeerd, 48 kHz | alleen als de overkant het kan; ruim 0,8 Mbit/s |

- **Inkomend op**: *auto* (het eerste vrije kanaal) of *geen* (inkomende gesprekken weigeren).
- **Aanmelden** elke zoveel seconden, en **Keep-alive** om de NAT-router open te houden.

### Adresboek en signalen

- **Adresboek**: vaste stations. *sip* = een nummer om te bellen; *rtp* = een vaste lijn met eigen poort, adres en
  poort van de overkant, codec en buffer.
- **Signalen**: commando's als een kanaal overgaat, verbonden is of weer stil wordt, bijvoorbeeld een lamp. In het
  commando wordt `{kanaal}` voip1 of voip2, en `{nr}` 1 of 2.
- **Gesprekken opnemen**: elk gesprek automatisch opnemen zodra er geluid binnenkomt.
- De commandobus kent `VOIP 1 DIAL <nummer>`, `VOIP 1 STATION <naam>`, `ANSWER`, `HANGUP`, `ONAIR`, `PFL` en
  `RECORD`.

## 11. Stream en titels

Venster **Stream**:

- **Start stream** en **Stop stream**.
- **Extra streamservers**, elk met een eigen MP3-stroom en kwaliteit:
  - Icecast: `host:poort/mount`;
  - SHOUTcast v1: `host:poort`;
  - SHOUTcast v2: `host:poort,sid`.
- **Titel**: in een eigen opmaak, met `{artiest}`, `{titel}`, `{categorie}`, `{type}`, `{bron}`, `{duur}`, `{tijd}`,
  `{datum}` en `{station}`. De titel gaat naar de stream, naar een tekstbestand (voor de website of RDS) en naar een
  webadres. Alleen de log verandert de titel; cartwall en overlays niet.
- **Programmalog als tekst**: één bestand per dag in een map, met een bewaartermijn.

## 12. Gebruikers en rechten

**Instellingen › Gebruikers**:

- **Nieuwe gebruiker**: naam, rol, pincode (4 tot 12 cijfers) en eventueel een **laatste dag** (voor een stagiair of
  invaller). De rollen zijn presentator, technicus en beheerder. Met gebruikers ingeschakeld zijn de instellingen
  alleen voor beheerders.
- Iemand die stopt kun je beter uitschakelen (**Mag zich aanmelden** uit) dan verwijderen. De naam blijft dan bij
  zijn uitzendingen staan.
- **Blokkeren na verkeerde pincodes**: na zoveel pogingen kan die naam zich een tijd niet aanmelden. **Opheffen**
  maakt een blokkade meteen ongedaan.
- Er blijft altijd een beheerder zonder einddatum.
- **Pincode kwijt en geen beheerder meer**: start de engine op die pc met `--users-off`. Tot de volgende start wordt
  er dan niets gecontroleerd.

## 13. Bedrijfszekerheid

### Reservekopie

**Instellingen › Systeem › Reservekopie**: één bestand met alle instellingen, klokken, planning, gebeurtenissen,
cartwalls, hardware, reclame, nieuws, presentatoren en de cuepunten van de bibliotheek. De audiobestanden zitten er
niet in; die bewaar je apart.

- **Reservekopie maken…** en **Reservekopie terugzetten…**. Terugzetten vervangt alles; wat er stond, blijft bewaard
  in een map in de datamap.
- **Elke dag automatisch een reservekopie maken**: op een vaste tijd, in een map (bij voorkeur een andere schijf of
  een NAS), met een bewaartermijn. Stond de pc uit, dan volgt de kopie zodra de engine weer draait.

### Reservestudio

**Instellingen › Reservestudio**: twee engines die op elkaar letten, elk met een eigen geluidskaart naar de zender of
de stream.

- Stel de ene in als **hoofd** en de andere als **reserve**, elk met het adres van de andere engine. Beide moeten op
  het netwerk te bereiken zijn (Op afstand › Engine op het netwerk).
- De reserve vraagt de hoofdstudio om de paar seconden hoe het gaat. Antwoordt die te lang niet (of is het daar
  stil, als je dat kiest), dan start de reserve zijn eigen automatisering. Eventueel start hij ook de stream.
- Speelt de hoofdstudio weer, dan geeft de reserve de uitzending terug: vanzelf na de gekozen tijd, of met
  **Teruggeven aan de hoofdstudio**. **Nu overnemen** start de reserve meteen.
- Tijdens het teruggeven spelen beide even; laat de mengtafel of de streamserver de juiste kiezen.
- **Commando's bij overnemen en teruggeven**: bijvoorbeeld een melding of een lamp.
- Gebruikt de reserve http terwijl de hoofdstudio niet op het netwerk staat, dan meldt de reserve dat.

### Poorten en firewall

**Instellingen › Systeem › Poorten** laat zien welke poorten de engine gebruikt: in of uit, alleen deze pc of het
netwerk. De engine zet zelf niets open in de firewall. Daaronder staat een PowerShell-opdracht die alleen de nodige
poorten opent; voer die als beheerder uit.

| Poort | Waarvoor |
|---|---|
| 8600 | de engine (http): studio, web en remote-app |
| 8443 | de engine (https) |
| 8601 | commandobus via TCP |
| 8602 | commandobus via OSC (UDP) |
| 8603 | commandobus via UDP |
| 5060 | SIP (UDP) |
| 5004 en 5006 | RTP van VoIP 1 en 2 (standaard) |

### Systeemlog

**Instellingen › Systeem › Systeemlog van de engine**: fouten en waarschuwingen van de engine zelf. Zet
**Uitgebreid bijhouden** alleen aan als je een probleem zoekt.

## 14. Meerdere studio's

### Centrale bibliotheek

**Instellingen › Centrale bibliotheek**:

- Eén engine is de **bron**. Die leest muziek en jingles in en meet ze: cues, loudness, golfvorm, BPM en toonsoort.
  Geef de bron een bedieningswachtwoord en zet **Engine op het netwerk** aan.
- De andere engines zijn **studio**. Ze nemen de bibliotheek over met alles wat gemeten is, via het adres en het
  bedieningswachtwoord van de bron.
- Kan een studio het bestand op de plek van de bron bereiken (een share), dan speelt hij het daarvandaan. Anders
  haalt hij het eerst op naar de eigen pc. Met **Alle bestanden naar deze pc halen** blijft een studio ook spelen
  als het netwerk wegvalt.
- Hoe vaak een nummer gespeeld is, blijft per studio. Eigen mappen werken daarnaast gewoon.

### Een studio op een andere pc

Zet op de engine **Engine op het netwerk** aan, en zet in de studio op de andere pc onder **Instellingen › Deze
studio** het adres van de engine. Zijn er geen gebruikers, geef de studio dan het bedieningswachtwoord via de
omgevingsvariabele `REA_ENGINE_KEY`.

## 15. Bediening en hardware

### De commandobus

Alles wat je in de studio kunt, kan ook met een tekstcommando: `NEXT`, `AUTO OFF`, `CART 1 5`, `MIC TOGGLE`,
`VOLUME log 80`, `STREAM START`, `DJ A STEM VOCALS OFF`… Het volledige overzicht staat in **Studio › Bediening en
sneltoetsen…** (tabblad Commando's, met **Uitvoeren** om te proberen).

Dezelfde commando's werken via:

- **HTTP**: `POST /api/command` met `{"text":"NEXT"}`, of een webadres per knop: `/api/do/next`.
- **TCP** (8601): één commando per regel; met wachtwoord eerst `AUTH <wachtwoord>`.
- **OSC** (8602): bijvoorbeeld `/rea/next` of `/rea/cart/1/5`.
- **UDP** (8603): één commando per pakket.
- **`rea-cmd`** naast de engine, bijvoorbeeld in een taakplanner.
- **Vaste tijden** in de planner, met de actie *Commando uitvoeren*.

### Sneltoetsen en gebeurtenissen

In **Bediening en sneltoetsen**:

- **Sneltoetsen**: een toets (F5, Ctrl+B, Ctrl+Alt+1) op een commando. Met **overal** werkt hij ook als een ander
  programma vooraan staat. Kies dan een combinatie die andere programma's niet gebruiken.
- **Gebeurtenissen**: bij een gebeurtenis (een item start, de microfoon gaat open, de stream valt weg) voert de engine
  een commando uit. Daarin worden `{artiest}`, `{titel}`, `{type}`, `{categorie}` en `{station}` ingevuld.
  Bijvoorbeeld: microfoon open → `OSC 192.168.1.20:8000 /onair 1`.
- **Eigen knoppen** (venster Eigen knoppen): knoppen op het scherm, elk met een naam, kleur en commando.

### Hardware

Venster **Hardware**:

- **Apparaten**: MIDI, seriële poorten, Livewire-GPIO en Ember+. De engine houdt elk actief apparaat verbonden en
  probeert het elke 5 seconden opnieuw als de verbinding wegvalt.
- **Knoppen**: een signaal van een apparaat (een noot, controller, GPI, Ember+-parameter of regel tekst) op een
  commando bij aan en bij uit. Druk een knop op het apparaat en klik hem aan bij **Laatste signalen** om hem over te
  nemen.
- **Lampjes**: een toestand (microfoon open, ON AIR, VoIP gaat over…) op een uitgang: een MIDI-noot, een
  Livewire-GPO, een Ember+-parameter of tekst op een seriële poort. Met **Test aan** en **Test uit** probeer je het.
- **Mackie Control** (bijvoorbeeld de iCON V1-M, met een uitbreiding zoals de V1-X):
  - acht strips volgen de mengkanalen;
  - de fader is het volume, met een motor die de studio volgt;
  - de draaiknop is de balans;
  - MUTE dempt, REC neemt op, en SELECT zet het kanaal naar de zender;
  - het display toont naam en stand, en de meters het niveau.

  In de DJ-modus wisselt het paneel vanzelf naar de DJ-indeling. De **transportknoppen** staan standaard uit, zodat
  een per ongeluk ingedrukte knop de zender niet stilzet.
- **iCON V1-M: aanraakknoppen**: **Indeling naar het paneel schrijven** zet REA's eigen indeling in het paneel, met
  de naam van wat elke knop doet. Sluit iMap eerst.

### DJ-controller

Venster **DJ-controller**: kies de MIDI-poort en klik **Controller toevoegen**. Druk bij een functie op **Leren** en
beweeg de knop, fader of het jogwiel. **Omkeren** draait de richting om. **Bewaren** maakt er een voorinstelling van
voor dezelfde controller op een andere plek.

## 16. Rapportage

- **As-run**: wat er werkelijk is uitgezonden, met wie er aangemeld was. **Exporteer CSV…**.
- **Muziekrapport (Buma/Stemra · Sena)**: elke uitzending van een nummer, met datum, tijd, duur, uitvoerende, titel,
  componist, label en ISRC. Ook als **overzicht per nummer**, als CSV of PDF.
- **Uitzendstatistiek**: het aandeel muziek in de zendtijd, nummers per uur, de meest gedraaide nummers en artiesten,
  de rotatie per categorie, en nummers die lang niet gedraaid zijn.
- **Uitzendbewijs** van reclame: in het venster Reclame.

## 17. Problemen oplossen

| Wat je ziet | Wat je doet |
|---|---|
| De studio zegt dat de engine niet bereikbaar is | **Studio › Engine starten**. Draait hij op een andere pc, controleer dan het adres bij Instellingen › Deze studio |
| Er komt geen geluid uit | Kijk bij Instellingen › Geluid welke kaart de uitzending gebruikt, en probeer de **Testtoon**. Kijk in Berichten of Windows de kaart zachter zette |
| De luisteraar hoort de PFL mee | De PFL en de uitzending staan op dezelfde kaart. Kies voor de PFL een andere kaart |
| Stilte-alarm in Berichten | Kijk wat er ON AIR staat en of de log loopt (AUTO). Start zo nodig met Volgende (F5) |
| Het geluid hapert | Maak de buffer groter (Instellingen › Geluid). Kijk of er iets zwaars op de pc draait |
| Een share is weg | De nummers blijven bewaard en komen terug zodra de share er weer is. Gebruik zo nodig een vervangende locatie |
| Een nummer wordt nooit ingepland | Wijs het teken in de bibliotheek aan: het zegt waarom. Kijk of het nummer aan staat |
| Het nieuws werd overgeslagen | Er was geen actueel nieuws (bestand te oud, geen koppen). Kijk in Berichten welke bron het was |
| Een AI-break komt niet | Kijk of de AI-presentator aan staat en de stemserver draait (venster AI-presentator). Valt een stem uit, dan spreekt de reservestem |
| Stems: "het scheidingsmodel ontbreekt" | Zet `htdemucs.onnx` in `engine\data\models` en start de engine opnieuw |
| Een beller is slecht te verstaan | Kijk naar de meting bij het kanaal (verlies, buffer). Maak de buffer groter, of kies een andere codec |
| Pincode kwijt en geen beheerder meer | Start de engine met `--users-off` en maak een nieuwe pincode |
| Een venster is zoek | **Indeling › Venster plaatsen…**, of **Standaardindeling herstellen** |
