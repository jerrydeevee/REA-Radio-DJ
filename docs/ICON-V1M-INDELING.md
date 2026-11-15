# iCON V1-M: wat zit waar, wat REA ermee doet

Overzicht van alle bedieningselementen van de V1-M. Per element staat erbij wat REA er nu mee doet en wat het
voorstel is. Bronnen: de V1-M-handleiding (pagina 10–15), de Bitwig-gids van iCON, de standaard
Mackie Control-noten, en wat iMap naar het paneel schrijft (zie [ICON-V1M-PROTOCOL.md](ICON-V1M-PROTOCOL.md)).

✅ = werkt en is op het paneel getest · 🟡 = gebouwd, niet op het paneel getest · 💡 = voorstel · — = doet niets

## Schermen

| Scherm | Wat het is | Nu | Voorstel |
|--------|------------|----|----------|
| Bovenste TFT (2 inch) | toont wat de software via Mackie Control naar het display stuurt | ✅ per fader een blokje: boven de resterende tijd, eronder de titel die op dat kanaal speelt (een lange titel schuift binnen het blokje). Speelt er niets, dan wat er klaarstaat: een geladen speler, of met `> ` het item uit de log dat daar als volgende komt (in elke modus). Log, standby, cart, overlay, PFL, DJ-master en decks; mic en VoIP blijven leeg. Tijdens het bladeren staat de keuze uit de log onderaan | — |
| Onderste TFT (1,14 inch, 8 vakjes boven de faders) | "tweede display" | 🟡 per strip naam en stand | — |
| LED-tijddisplay | cijfers 9–2 zichtbaar | ✅ de klok (uu.mm.ss), punten na uren en minuten | — |
| Meters (8 × mono, master stereo) | Mackie-meters | ✅ niveau per kanaal, 🟡 master = programma-uitgang | — |
| Kleurbalk | kleur per strip | ✅ kleur per soort kanaal; fel als het op de zender is, anders donker; **knippert** zolang er geluid door het kanaal gaat (log-item, speler, carts, open mic, deck) | — |
| Aanraakscherm (7 inch, 24 knoppen × 5 lagen) | door REA geschreven indeling | ✅ vijf lagen, zie hieronder | — |

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
| LOOP | 0x56 | ✅ modus AUTO → SEMI → HAND (lampje aan / knippert / uit) | — |
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

## Aanraakscherm: lagen ✅

*Indeling naar het paneel schrijven* (Hardware) zet alle vijf lagen op noten (kanaal 2–6, noot 0–23) en vult ze met
REA's indeling. Een knop die je zelf op iets anders dan een cart hebt gezet, blijft zoals hij is.

| Laag | Kanaal | Knop 1–16 | Knop 17 | Knop 18 | Onderste rij (knop 19–24) |
|------|--------|-----------|---------|---------|---------------------------|
| Blauw | 2 | cart 1–16 (wand 1, pagina 1) | vrij | Carts stop | schakelaars |
| Groen | 3 | cart 17–32 (pagina 2) | vrij | Carts stop | schakelaars |
| Oranje | 4 | cart 33–48 (pagina 3) | vrij | Carts stop | schakelaars |
| Paars (U1) | 5 | cart 49–64 (pagina 4) | vrij | Carts stop | schakelaars |
| Rood (U2) | 6 | functies (zie hieronder) | | | |

**Schakelaars** (op elke cartlaag dezelfde): Auto · Semi · Hand · Mic · Volgende · Overslaan.

**Knoppen die blijven branden** ✅: REA stuurt de noot van een knop terug naar het paneel (velocity 127 = aan,
0 = uit) zolang wat hij doet loopt: een cart die speelt, de modus die aan staat (Auto/Semi/Hand: de ene gaat uit als
de andere aangaat), de open mic, een speler die speelt, DJ op de lucht, automix. Volgende, Overslaan en Carts stop
doen iets eenmalig en branden niet. Op het paneel getest: werkt.

**Functies op U2:** Auto, Semi, Hand, Volgende, Overslaan, AI-break · Mic, Speler A–D, AI-presentator · Carts stop,
Overlay stop, PFL stop, DJ op de lucht, Automix, Log vullen · VoIP 1 opnemen/ophangen/zender, VoIP 2 opnemen/ophangen/zender.

Aangenomen: de knoppen staan in 4 rijen van 6, genummerd van links naar rechts en van boven naar onder. Of U1 en U2
noten sturen in plaats van sneltoetsen (slots #138–#185) moet nog op het paneel blijken.

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
dicht stond (cartwall, overlay, standby, PFL, decks). Een leeg of afgelopen deck, een gesloten Mic 1, de cartwall
na een cart of jingle, de AI-presentator na een break en een VoIP-kanaal zonder gesprek gaan weer naar 0
(een VoIP-kanaal gaat open als het gesprek of de lijn verbonden is);
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

**De flow onder de faders.** Alles uit de programmalog speelt altijd onder een fader, ook volautomatisch:
muziek om en om onder Speler A en B (fader 1 en 2), een jingle onder de Cartwall, een AI-break of voicetrack onder
de AI-presentator (fader 4). De fader van dat kanaal gaat open als het item begint en dicht als het klaar is. In de
studio staat het item in de spelerkaart van Speler A/B ("uit programmalog"); kleuren: A rood, B geel, C blauw.

**Modi** (studio: Auto / Semi / Hand, iCON: LOOP, commando `MODE AUTO|SEMI|HAND|CYCLE`):

| Modus | Wat er gebeurt |
|-------|----------------|
| AUTO | De log loopt helemaal vanzelf. |
| SEMI | De log loopt vanzelf; de DJ valt in met jingles, tracks, mic of AI. Start de DJ een eigen track op een speler, dan wacht de log na wat hij nu speelt en gaat verder als die track klaar is. |
| HAND | De DJ start elk item. Het volgende item staat met `> ` op het schermpje van zijn fader; fader open of PLAY start het. |

Een speler die je zelf laadt (Speler C/D of A/B als ze leeg zijn): fader open = start, dicht = stop; SOLO luistert
voor op de PFL. Uit te zetten met *Live met de hand* bij *Hardware › Mackie*.