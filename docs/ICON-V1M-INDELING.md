# iCON V1-M: wat zit waar, wat REA ermee doet

Overzicht van alle bedieningselementen van de V1-M. Per element staat erbij wat REA er nu mee doet en wat het
voorstel is. Bronnen: de V1-M-handleiding (pagina 10–15), de Bitwig-gids van iCON, de standaard
Mackie Control-noten, en wat iMap naar het paneel schrijft (zie [ICON-V1M-PROTOCOL.md](ICON-V1M-PROTOCOL.md)).

✅ = werkt en is op het paneel getest · 🟡 = gebouwd, niet op het paneel getest · 💡 = voorstel · — = doet niets

## Schermen

| Scherm | Wat het is | Nu | Voorstel |
|--------|------------|----|----------|
| Bovenste TFT (2 inch) | toont wat de software via Mackie Control naar het display stuurt | ✅ per fader een blokje: boven de resterende tijd, eronder de titel die op dat kanaal speelt (een lange titel schuift binnen het blokje). Log, standby, cart, overlay, PFL, DJ-master en decks; mic en VoIP blijven leeg. Tijdens het bladeren staat de keuze uit de log onderaan | — |
| Onderste TFT (1,14 inch, 8 vakjes boven de faders) | "tweede display" | 🟡 per strip naam en stand | — |
| LED-tijddisplay | cijfers 9–2 zichtbaar | ✅ de klok (uu.mm.ss), punten na uren en minuten | — |
| Meters (8 × mono, master stereo) | Mackie-meters | ✅ niveau per kanaal, 🟡 master = programma-uitgang | — |
| Kleurbalk | kleur per strip | 🟡 kleur per soort kanaal; fel als het op de zender is (een deck: als het speelt), anders donker | — |
| Aanraakscherm (7 inch, 24 knoppen × 5 lagen) | door REA geschreven indeling | ✅ blauw, groen en oranje = cartwall met cartnamen | 💡 paars (U1) en rood (U2) ook gebruiken, zie hieronder |

## Kanaalstrips (8×)

| Knop | Mackie-noot | Uitzending (nu) | DJ-indeling (voorstel) |
|------|-------------|-----------------|------------------------|
| Motorfader | pitch bend 0–7 | ✅ volume van het kanaal, de motor volgt de studio | deck A–D: kanaalfader van het deck |
| Aanraken fader | 0x68–0x6F | ✅ de motor laat de fader los zolang je hem aanraakt | zelfde |
| Draaiknop (V-pot) | CC 0x10–0x17 | ✅ balans | gain van het deck (of filter) |
| Draaiknop indrukken | 0x20–0x27 | ✅ balans naar het midden | gain op 0 dB |
| REC | 0x00–0x07 | 🟡 opnemen van het kanaal (nog niet op het paneel getest) | CUE van het deck |
| SOLO | 0x08–0x0F | — | 💡 **PFL** van het kanaal of deck (voorbeluisteren) |
| MUTE | 0x10–0x17 | ✅ dempen | PLAY/PAUZE van het deck |
| SELECT | 0x18–0x1F | ✅ naar de zender aan of uit | SYNC van het deck |

## Masterdeel en navigatie

| Knop | Mackie-noot | Nu | Voorstel |
|------|-------------|----|----------|
| Masterfader | pitch bend 8 | ✅ het kanaal dat bij *Masterfader* is gekozen | DJ: de master van de DJ-tafel |
| Master/Assign | intern | — (schakelt in het paneel) | — |
| Flip | 0x32 | ✅ wisselt tussen de indelingen Uitzending en DJ (lampje = DJ) | — |
| Lock | intern | faders vast (paneel zelf) | — |
| Fader ◀ ▶ | 0x30 / 0x31 | ✅ één kanaal opschuiven | — |
| Bank ◀◀ ▶▶ | 0x2E / 0x2F | ✅ acht kanalen opschuiven | — |
| Jogwiel | CC 0x3C | ✅ door de log bladeren (PLAY start de keuze), ⚠️ scrollt ook als muis | DJ: door de bibliotheek, of het deck verschuiven (pitch bend) |
| Pijlen ▲ ▼ | 0x60 / 0x61 | — | 💡 één stap door de log, zoals het jogwiel |
| Pijlen ◀ ▶ | 0x62 / 0x63 | — | 💡 een andere pagina van de cartwall (1–4) |
| Navi, Focus, Zoom, Shuffle | intern of HID | het paneel stuurt dan pijltjes of het muiswiel naar Windows | — (*Focus* zet het jogwiel op muiswiel) |
| User A / B | 0x66 / 0x67 | — | 💡 vrij te koppelen in Hardware |

## Transport

Werkt alleen als *Transportknoppen bedienen de uitzending* aan staat.

| Knop | Mackie-noot | Nu | Voorstel |
|------|-------------|----|----------|
| PLAY | 0x5E | ✅ NEXT, of de keuze van het jogwiel | — |
| STOP | 0x5D | 🟡 STOP (uitfaden) | — |
| LOOP | 0x56 | 🟡 automatisch of live-assist | — |
| ◀◀ | 0x5B | — | 💡 PFL van het volgende item in de log |
| ▶▶ | 0x5C | — | 💡 SKIP (snel uitfaden) |
| REC | 0x5F | — | 💡 microfoon aan/uit (talkover) |

## Automatiseringsknoppen

| Knop | Mackie-noot | Nu | Voorstel |
|------|-------------|----|----------|
| Read | 0x4A | — | 💡 standby-speler 1 start/stop |
| Write | 0x4B | — | 💡 standby-speler 2 start/stop |
| Touch | 0x4D | — | 💡 PFL-speler start/stop |
| Latch | 0x4E | — | 💡 alle carts stoppen |
| Trim, Off | 0x4C, (0x4A) | blijven donker op de V1-M | — |

De lampjes van deze knoppen kan REA laten branden als de speler loopt.

## Aanraakscherm: lagen

| Laag | Stuurt | Nu | Voorstel |
|------|--------|----|----------|
| Blauw | kanaal 2, noot 0–23 | ✅ cart 1–24 | — |
| Groen | kanaal 3, noot 0–23 | ✅ cart 25–48 | — |
| Oranje | kanaal 4, noot 0–23 | ✅ cart 49–64, laatste knop = carts stoppen | — |
| Paars (U1) | nu sneltoetsen van Bitwig | — | 💡 **uitzending**: NEXT, SKIP, BREAK, AUTO, MIC, STREAM, VoIP opnemen/ophangen, … (kanaal 5) |
| Rood (U2) | nu sneltoetsen van Bitwig | — | 💡 **DJ**: deck A–D play/cue/sync, hotcues, automix, crossfader links/midden/rechts (kanaal 6) |

Het aanraakscherm heeft vijf lagen. Met U1 en U2 erbij kan de cartwall naar 5 × 24 = 120 knoppen, of U1/U2 krijgen de
functies hierboven. Die keuze is nog open.

Of de lagen U1 en U2 ook noten kunnen sturen in plaats van sneltoetsen, moet nog op het paneel getest worden.
Volgens de handleiding kan het, maar daarvoor moeten de 48 slots #138–#185 andere bytes krijgen.

## DJ-indeling ✅

Het paneel heeft twee standen, **Uitzending** en **DJ**. Gaat de DJ-tafel op de zender (*DJ ON AIR*, niet alleen PFL),
dan wisselt het paneel vanzelf naar DJ. Gaat de tafel eraf, dan gaat het paneel terug naar Uitzending. Dat gebeurt
alleen als het paneel ook vanzelf naar DJ was gegaan: een keuze met FLIP blijft staan.

**Faderstart** staat aan, en kan uit bij *Hardware › Mackie*:
- Een dichte deckfader die je verder dan 10 % opent, start het deck. Een tikje start het deck niet.
- Een fader die dichtgaat, pauzeert het deck.
- Start je een deck op het scherm terwijl de fader dicht staat, dan gaat de fader vanzelf open en beweegt de motorfader mee.

Lukt een deckknop niet, dan staat de reden in het logboek (bijv. SYNC zonder ander spelend deck).
Een knop zonder functie verschijnt in het logboek met zijn noot (`knop 4Ah: geen functie`). Zo vind je terug wat
een knop stuurt.

| Strip | Uitzending | DJ (gebouwd) |
|-------|------------|----|
| 1 | Log | Deck A |
| 2 | Standby | Deck B |
| 3 | Cart | Deck C |
| 4 | Overlay | Deck D |
| 5 | DJ | DJ-master |
| 6 | Mic | Mic |
| 7 | VoIP 1 | PFL |
| 8 | VoIP 2 | Log |
| master | gekozen kanaal | gekozen kanaal |

**Automix in de DJ-indeling:** LOOP zet de automix aan of uit (het lampje brandt als hij aan staat), PLAY mixt
meteen naar het volgende nummer. Dat werkt als *Transportknoppen bedienen de uitzending* aan staat. De automix
gebruikt 2 decks (A, B) of 4 (A t/m D om de beurt). Met *Kanaalfaders* mixt hij met de deckfaders, zodat de
motorfaders meebewegen; de crossfader telt dan niet mee.

**SYNC** volgt alleen een deck dat minder dan 8 % in tempo verschilt (of half/dubbel tempo). Is het andere deck weg,
dan glijdt het deck in 8 seconden terug naar zijn eigen tempo.

**Faders volgen het gebruik** (in de hele mixer): een kanaal waarop iets start, krijgt zijn fader open als die
dicht stond (cartwall, overlay, standby, PFL, decks). Een leeg of afgelopen deck en een gesloten Mic 1 gaan naar 0;
de mic komt bij openen terug op zijn oude stand.

## Live-indeling ✅

| Strip | Live |
|-------|------|
| 1 | Speler A |
| 2 | Speler B |
| 3 | Cartwall |
| 4 | AI-presentator (de AI-breaks) |
| 5 | Mic 1 |
| 6 | Mic 2 (eerste ingang onder *Hardware › Ingangen*) |
| 7–8 | VoIP 1 en 2 |
| daarna (Bank) | Speler C/D, Overlay, DJ (geen automatiseringsfader: live nemen de spelers het over) |

Elke standby-speler heeft een eigen kanaal. **Met de hand** (automatisering uit) vult de log speler A en B, een
klare AI-break gaat naar de AI-presentator. Fader open = speler start, dicht = stop; PLAY start de speler die het
langst klaarstaat; SOLO luistert het nummer van die speler voor op de PFL. Uit te zetten met *Live met de hand*
bij *Hardware › Mackie*.