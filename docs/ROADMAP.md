# Rondeplan

Ronde 1 staat (engine-service, desktop-studio, bibliotheek met cues, klokken en vaste tijden, spelers, cartwalls,
mixer, as-run, REST/WebSocket, tray-icoon). Hieronder de volgende rondes. Alles wordt eigen ontwerp: functies zoals
een professioneel playoutsysteem ze nodig heeft, zonder teksten, namen, schermen of bestandsformaten van andere
pakketten over te nemen.

## Ronde 2a — Bediening, planning en regels (klaar)

| Onderdeel | Wat | Status |
|---|---|---|
| Commandobus | Eén lijst tekstcommando's (`NEXT`, `AUTO ON`, `CART 1 5`, `MIC TOGGLE` …) die overal hetzelfde werkt | klaar |
| Bediening op afstand | TCP (regel-voor-regel, met wachtwoord), OSC (UDP), HTTP (`/api/command`) | klaar |
| Sneltoetsen | Vrij in te stellen in de studio; elke toets stuurt een commando | klaar |
| Planner-uitbreiding | Vaste tijden kunnen ook een commando uitvoeren, een herinnering tonen of een alarm geven | klaar |
| Streambewaking | Externe stream (Icecast/SHOUTcast-URL) in een tijdvenster bewaken; is hij live, dan neemt hij het programma over (pauzeren) of speelt hij eroverheen | klaar |
| Studio-UX | Aftelbalk die leegloopt (oranje bij 20 s, rood knipperend bij 10 s), intro-teller, grote aanraakknoppen (groen = start, rood = stop), weergaven Automatisering (rustig bewaken) en Live-assist (volledige studio) | klaar |
| Scriptregels | Regels op basis van het vorige nummer: onderdrukken of uitstellen (bijv. niet twee keer dezelfde categorie, maximaal N op rij) | klaar |

## Ronde 2b — Presentatie

- **Voicetracking**: opnemen tussen twee items in de log, in drie stappen (start = vorige item loopt uit,
  volgende = volgend item start, stop). Opnames als gewone items in de log; ook voor AI-breaks te gebruiken.
- **Overgangseditor**: grafisch de overgang tussen twee items instellen tot op de milliseconde, met inserts
  (voice-overs), volume-envelopes en direct opnemen. Wat je ziet en hoort, gaat de lucht in.
- **Nieuwsbeheer**: nieuwsblokken, RSS-bronnen, tekst voor de presentator, nieuws-opener/-sluiter als vaste tijd.

## Ronde 3 — Koppelingen en reclame

- **Externe planning**: logbestanden van muziekplanners importeren via instelbare import-profielen
  (kolommen/vaste breedte/CSV), met automatisch aanmaken van planner-items; bibliotheek exporteren voor de
  muziekplanner.
- **Database-bibliotheken**: bibliotheek uit een eigen databasetabel of -view (ODBC) plus metadata-opzoeking.
- **Reclamebeheer**: adverteerders, campagnes, spots, blokken; blokken automatisch in de overlay-planner;
  bewijs van uitzending als basis voor facturatie; import van traffic-logs.
- **Hardware-bediening**: MIDI, seriële I/O, GPIO-kaarten en mengtafelprotocollen (o.a. Ember+), via dezelfde
  commandobus.

## Ronde 4 — Meerdere studio's en op afstand

- **Gebruikers en rechten**: gebruikers, profielen, rechten, wachtwoorden; per gebruiker eigen indelingen.
- **Multi-studio**: een studio vanuit een andere bedienen (planner, log, mixer), master/slave met hartslag en
  automatische overname.
- **Centrale bibliotheek**: bibliotheekserver in het netwerk; studio's halen media op afstand.
- **Voicetracking op afstand**: logsjabloon en audio synchroniseren naar thuis (gecomprimeerd), opnemen, terugsturen.
- **Remote-app**: Android-tablet (bedienen) en telefoon (bewaken), op de bestaande API.
