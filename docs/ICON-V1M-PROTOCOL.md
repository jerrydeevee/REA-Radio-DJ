# iCON V1-M configuratieprotocol (SysEx)

Afgeleid uit wat iMap (`V1M_V1X_iMap.exe`) naar het paneel stuurt, vastgelegd met
alleen-lezen Frida-haken op winmm (`midiOutLongMsg`/`midiOutShortMsg`). Niet officieel
gedocumenteerd; alles hieronder is waargenomen gedrag.

## Poort

iMap schrijft de configuratie naar de **vierde** MIDI-uitgang van het paneel:
`MIDIOUT4 (iCON V1-M)`. De Mackie-besturing zelf loopt over poort 1.

## Kop

Elk SysEx-bericht begint met `F0 1D 03 10 09 <cmd>` en eindigt met `F7`
(`09` = model V1-M).

## Een volledige schrijfronde

iMap stuurt bij **elke** wijziging de hele configuratie, nooit een los veld:

| # | Bericht | Betekenis |
|---|---------|-----------|
| 1 | `24 <t1 t2 t3> 00 00 00 00 00 00` | begin; `t1..t3` is per ronde willekeurig (geen checksum) |
| 2 | short `EF 7F 7F` | |
| 3 | `2C 21 04` | |
| 4 | 8× `25 21 <pagina 00-07> 08 <28 slots × 8 bytes>` | toewijzingen, ~10 ms tussenruimte |
| 5 | 10× (short `EF 7F 7F`, dan `26 01 01 <pagina 00-09> 0C 02 08 <12 × 16 ASCII>`) | teksten, ~35 ms tussenruimte |
| 6 | `2E 21 00` | |
| 7 | short `EF 7F 7F`, short `EC 22 21` | |
| 8 | `27 <t1 t2 t3> 00 …` | einde; zelfde token als het begin |

Opslaan als gebruikersstandaard: short `EF 7F 7F`, dan short `EC 2D 01`.

De `21` in `2C`/`25`/`2E`/`EC 22 21` is vermoedelijk het nummer van de actieve
DAW-sleuf; nog niet bevestigd.

## Toewijzingsslot (8 bytes)

Slotnummer = pagina × 28 + slot.

| Bytes | Betekenis |
|-------|-----------|
| `09 09 <kanaal 0-based> <noot> 7F 00 00 00` | noot (bv. kanaal 2 noot 0 = `09 09 01 00 7F 00 00 00`) |
| `0B 0B 00 <cc> …` | controller; jogwiel = `0B 0B 00 3C 41 01 00 00` op #204 (pagina 7 slot 8) |
| `0E 0E <n> 00 …` | pitch bend (faders) |
| `40/41/43/45 00 00 <toets>` | vermoedelijk toetsenbordsneltoets (HID) |
| `00 × 8` | leeg |

### Aanraakknoppen

24 knoppen per laag:

| Laag | Slots | Tekstpagina's |
|------|-------|---------------|
| blauw | #66–#89 (pagina 2 slot 10 … pagina 3 slot 5) | 0–1 |
| groen | #90–#113 | 2–3 |
| oranje | #114–#137 | 4–5 |
| paars (U1) | #138–#161 | 6–7 |
| rood (U2) | #162–#185 | 8–9 |

## Teksten

Per tekstpagina 12 knoppen van 16 ASCII-tekens: 2 regels van 8. Pagina's 6–9 zijn U1 en U2
(bij iMap de teksten van zijn sneltoetsen). iMap vult bij een noottoewijzing zelf `Note N` in.

## Nog onbekend

- Wat `21` precies is en hoe je een DAW-modus of gebruikerssleuf kiest.
- Of het paneel iets terugstuurt (bevestiging); tijdens de opnames kwam er niets binnen.
- Hoe het muiswiel (HID) van het jogwiel uitgaat. #204 stuurt wel CC 60 (REA ontvangt het), maar het paneel stuurt tegelijk ook het muiswiel.

## Getest (15-11-2026)

REA schrijft de indeling zelf (`IconV1M` en `IconPanel`). Dat werkt terwijl iMap draait. Teksten en noten komen aan. Na `EC 2D 01` blijft de indeling staan als de USB-kabel eruit en er weer in gaat. Een bevestiging van het paneel is niet nodig.

## Schermen en kleuren (poort 1, naast Mackie Control)

Bronnen: het officiële Logic-script van iCON (v0.9.1, `iCON V1-M.device/config.lua`) en de opensource-scripts van
bjoluc voor Cubase (`IconColorManager`, `LcdManager`). Het scherm van elk bericht is op het paneel nagekeken (15-11-2026).

| Bericht | Scherm | Inhoud |
|---------|--------|--------|
| `F0 00 00 66 14 12 <pos> <ASCII> F7` | bovenste TFT (2 inch) | gewoon Mackie-display, 2 rijen van 56 tekens (`pos` = rij × 56 + kolom) |
| `F0 00 02 4E 15 13 <pos> <ASCII> F7` | klein display boven de faders (8 vakjes) | tweede display, ook 2 × 56 tekens, 7 per strip; ✅ getest |
| `F0 00 02 4E 16 14 <8 × R G B> F7` | kleurbalk | kleur per strip, 0–127 per kleur; ✅ getest |
| `D1 <(id << 4) + niveau>` | mastermeter | id 0 = links, 1 = rechts; niveau 0–12 |
| `B0 <40h + n> <teken>` | LED-tijddisplay | cijfer n = 0–9 (tijd), 10–11 (toewijzing); teken `30h` + cijfer, `20h` = leeg, `+40h` = punt erachter; ✅ klok getest, cijfers 9–2 zichtbaar, de punten staan niet vast |

In de Logic-scripts sturen de V1-X-extenders hun display ook met `14` (niet met `15` zoals bij een Mackie XT). Elk
paneel heeft zijn eigen USB-poort, en daaraan herkent de software welk paneel het is. REA stuurt een extender nu `15`.
Dat moet met een V1-X nog getest worden.
