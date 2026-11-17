# Wijzigingen

## 1.2.0 (in ontwikkeling)

### Nieuw
- **Geplande opnames** van een ingang of internetstream (bijv. elke werkdag om 12:00 het regionale nieuws), direct in
  de bibliotheek; met *Vorige vervangen* blijft het hetzelfde item met steeds de nieuwste opname. Venster *Opnames*.
- **Noodopname**: één knop of `RECORD TOGGLE` neemt alles op wat binnenkomt, elk kanaal apart; stille kanalen vallen weg.
- **Uitzending gemist en podcast**: per programma na elke uitzending een aflevering uit de uitzendopname, met een
  RSS-feed op `/podcast/<id>/feed.xml` en een kopieermap voor de website.
- **Plaatshouders**: een item (bijv. een reclamespot) alvast inplannen terwijl het geluid nog moet komen, met een
  waarschuwing een dag vooraf en een alarm als het te laat is. Databaseschema 26.
- **Meldingen per mail** (SMTP) bij stilte, overname door de reservestudio, een mislukte reservekopie en meer, met
  een grens per onderwerp; het wachtwoord wordt versleuteld bewaard.
- **Database-controle vóór een upgrade**: controle en kopie vooraf; een beschadigde database blijft onaangeroerd, met
  een duidelijke melding (venster, `data\DATABASE-PROBLEEM.txt`). Ook los: `REA-Radio-DJ.exe --check-db`.
- **Alleen het praten opnemen**: een opname van de microfoon of een ingangskanaal die alleen loopt als het kanaal
  open is en er geluid is (drempel en nalooptijd), één MP3 per uur.
- **Bewaking van de geluidskaart**: zet Windows of een ander programma de kaart van de zender zachter of dempt het hem,
  dan komt er een melding, of de engine zet hem meteen terug.
- **Presentatortekst** per item, groot in de Live-balk zolang het item speelt.
- **Speelduur per soort en categorie**: een langste speelduur, en korte items die zonder faden uitspelen.
- **Zachte start met grens**: een zacht blok wacht hoogstens de ingestelde tijd op het einde van het item.
- **Gebruikers met een laatste dag**, en een instelbare blokkade na verkeerde pincodes die een beheerder kan opheffen.
- **Programmalog afdrukken of exporteren** als PDF en CSV, met de verwachte begintijden.
- **`rea-cmd`**: commando's naar de engine vanaf de opdrachtregel of uit een script.
- **ASIO-bewaking**: stopt een ASIO-kaart tijdens het spelen, dan start de engine hem opnieuw, met een melding; komt
  hij niet terug, dan een waarschuwing en elke 10 seconden een nieuwe poging.
- **Testplan** (`docs/TESTPLAN.md`) voor luistertests en echte apparatuur, met een lijst geteste apparatuur.
- **Elke dag een reservekopie** van de database op een vaste tijd, in een eigen map, met een bewaartermijn; de
  nieuwste blijft altijd staan, en een gemiste nacht wordt ingehaald.
- **Poortenoverzicht** in *Instellingen › Systeem*, met een firewallopdracht voor de poorten die open moeten.
- **Afspeellijst als bron in een klok** (M3U, M3U8, PLS, XSPF): op volgorde of willekeurig, alleen bestanden uit de
  bibliotheek; de lijst wordt opnieuw gelezen als hij verandert. Databaseschema 24.
- **Afstandsbedieners met eigen rechten**: een tablet, knoppenkast of ander programma krijgt een eigen sleutel (of
  wordt herkend aan zijn vaste adres) en mag alleen de gekozen groepen commando's gebruiken, via TCP, UDP, OSC en
  `/api/do`. Over HTTP mag hij verder alleen meekijken. Sleutels worden alleen als hash bewaard en één keer getoond.
- **Uit de uitzending knippen**: een stuk uit de uitzendopname (van-tot op tijd, ook over het uur heen) als nieuw item
  in de bibliotheek, met titel en artiest, bij de jingles in de categorie Opnamen.
- **Stems op de DJ-decks**: zang, drums, bas en de rest los aan of uit. Ze worden op de achtergrond op de processor
  gemaakt (HT-Demucs), en bewaard tot 20 GB. Het model (`data\models\htdemucs.onnx`) zit niet in de setup.
- **Opus en G.722** voor de VoIP-lijnen en SIP, naast G.711 en PCM. Bij Opus vult de codec zelf een verloren pakket op.
- **Speelwijze AUTO, SEMI en HAND**, en de log onder de faders: muziek op speler A/B, jingles op de cartwall en
  AI-breaks op de AI-presentator, elk met een eigen fader die meebeweegt.
- **Automix op de DJ-tafel** met 2 of 4 decks, ook via de kanaalfaders.
- **Centrale bibliotheek**: één engine leest de muziek in en meet hem, de andere studio's nemen dat over.
- **Https op het netwerk** met een eigen certificaat van het station (poort 8443), en **voicetracking vanuit de
  browser**.
- **Reservestudio**: de hartslag gaat ook via https, met een waarschuwing als de hoofdstudio niet op het netwerk staat.
- **iCON V1-M en andere Mackie-panelen**: REA's eigen indeling in het paneel, schermen met de nummers per fader, een
  DJ-indeling met faderstart en de jogwheel door de log.
- **Instellingen › Besturing** (hardware, DJ-controller, sneltoetsen), en de speelwijze bij het starten van de engine.
- AI-breaks worden naast de automatisering klaargemaakt; is er een te laat, dan komt er snel een korte tekst met de
  reservestem.
- Geluidskaarten worden ook op naam onthouden, en de microfoon kan op één kanaal (mono).
- **Handleiding** (`docs/HANDLEIDING.md`) voor de studio en voor de beheerder.

### Opgelost
- De engine stopt snel, ook als er een studio verbonden is.
- In een laag venster lag de analoge klok over de tellers, en de lijst van de bibliotheek over het zoekveld.
- In de as-run stond bij items zonder gemeten speelduur een losse "s".
- Foutmeldingen van de engine met een backslash (een map) kwamen met dubbele backslashes in de studio.

## 1.1.0 (13 november 2026)

Tweede openbare uitgave, de eerste met een Windows-setup. Status: proof of concept, nog niet bedoeld voor een echte
zender. Deze versie is eerder dezelfde dag als release candidate 1.1.0-rc.1 verschenen.

### Nieuw
- **Venster Uitsturing**: het programma met piek, loudness (kort en geïntegreerd), true peak en begrenzer, en per
  mengkanaal een grote meter met een lampje OPEN, DICHT, STIL of MIC OPEN en een signaalstip.
- **LED-meters** in dBFS (−48 tot 0) met piek-vasthouden, in de decks, de mixer, de DJ-strip en de bovenbalk.
- **Signaalbalk op het deck in de lucht**: een lampje dat met het geluid meegaat en de melding *geen geluid*.
- **Reservestudio**: een tweede engine neemt de uitzending over als de hoofdstudio uitvalt, en geeft hem daarna terug.
- **Pagina voor telefoon en tablet** (`/remote/`): wat er speelt en komt, met grote knoppen voor start, overslaan en carts.
- **Gebruikers, rollen en studioslot**: aanmelden met naam en pincode, rollen presentator, technicus en beheerder,
  en *Studio vergrendelen* (Ctrl+L).
- **Beveiliging van het netwerk**: lezen blijft open, maar elke actie via het netwerk vraagt een aangemelde
  gebruiker of het bedieningswachtwoord.
- **AI-nieuws en weerbericht** als nieuwsblok, met bronvermelding, eigen stem en nieuwstune.
- **Stemmendatabase**: eigen stemmen opnemen of uploaden, met rollen voor presentator, nieuws en weer; de
  Higgs-server kan met de engine mee starten.
- **Studioklokken** in drie stijlen: analoog en digitaal samen, LED met secondeblokjes, en een uurklok.
- **Mackie Control**: het mengpaneel bedienen met een paneel met motorfaders, ook met uitbreidingen ernaast.
- **Inrichthulp** bij de eerste start, en een **melding van een nieuwere uitgave** (Info › Zoeken naar updates).
- **Windows-setup** (`REA-Radio-DJ-1.1.0-setup.exe`) naast de zip: installatie per gebruiker zonder
  beheerdersrechten, met Startmenu, en bijwerken over een oude versie heen.

### Verbeterd
- Nummers worden met AcoustID eerst **op geluid** herkend, daarna op de tags; nummers die straks in de log komen,
  worden vooruit opgezocht.
- **Losse bestanden** uit de Verkenner komen in de categorie *Losse bestanden* en krijgen een hoes en metadata.
- De DJ-weergave met vier decks past op 1920 × 1080; de instellingen van de DJ-tafel blijven bewaard na een herstart.
- De menu's Vensters en Zwevend zijn gegroepeerd; zwevende vensters en werkbladen krijgen netjes de focus.
- De deck-kaarten hebben een gelijkmatige gekleurde rand, die op elke schermschaal netjes met de hoeken meeloopt.

### Opgelost
- Het inlezen van de bibliotheek kon de playout-engine laten haperen.
- De studio kon vastlopen bij het wisselen van de knopstijl.
- Een lege lijst na het verversen van de bibliotheek, en de schakelaar muziek/jingle.
- `folder.jpg` op een gedeelde map telt alleen nog als hoes in een echte albummap.

### Nog open
- SIP bij een echte aanbieder; Opus en G.722.
- Ember+ en Mackie Control tegen een echt apparaat.
- De lampjes van een hardware-mengtafel in Uitsturing (hardwarelaag), en meters per fysieke uitgang.
- DJ-modus beluisteren.

## 1.0.1 (11 november 2026)

De eerste openbare revisie. Zie de [uitgave](https://github.com/jerrydeevee/REA-Radio-DJ/releases/tag/v1.0.1).
