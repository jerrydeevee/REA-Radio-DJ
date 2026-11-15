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

## Teksten

Per tekstpagina 12 knoppen van 16 ASCII-tekens: 2 regels van 8. Pagina's 6–9 bevatten
andere labels. iMap vult bij een noottoewijzing zelf `Note N` in.

## Nog onbekend

- Wat `21` precies is en hoe je een DAW-modus of gebruikerssleuf kiest.
- Of het paneel iets terugstuurt (bevestiging); tijdens de opnames kwam er niets binnen.
- Hoe het muiswiel (HID) van het jogwiel uitgaat. #204 stuurt wel CC 60 (REA ontvangt het), maar het paneel stuurt tegelijk ook het muiswiel.

## Getest (15-11-2026)

REA schrijft de indeling zelf (`IconV1M` en `IconPanel`). Dat werkt terwijl iMap draait. Teksten en noten komen aan. Na `EC 2D 01` blijft de indeling staan als de USB-kabel eruit en er weer in gaat. Een bevestiging van het paneel is niet nodig.
