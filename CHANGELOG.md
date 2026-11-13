# Wijzigingen

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
