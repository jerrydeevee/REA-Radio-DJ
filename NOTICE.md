# Licentie en derde partijen

REA-Radio-DJ is vrije software: je mag het verspreiden en aanpassen onder de voorwaarden van de
**GNU General Public License versie 3** of (naar keuze) een latere versie. De volledige tekst staat in [LICENSE](LICENSE).

## Aanvullende toestemming voor BASS (GPL-3.0, sectie 7)

REA-Radio-DJ gebruikt de audiobibliotheken **BASS** van un4seen developments (bass, bassmix, bassenc,
bassenc_mp3, bassloud, bassflac). BASS is geen vrije software. Daarom geldt de volgende aanvullende toestemming:

> Als aanvullende toestemming onder sectie 7 van de GNU GPL versie 3 krijg je het recht om dit programma te
> combineren of te linken met de BASS-bibliotheken van un4seen developments (of een aangepaste versie daarvan) en
> het resulterende werk te verspreiden. De broncode van BASS hoeft daarbij niet beschikbaar te worden gesteld.
> De broncode van REA-Radio-DJ zelf valt wel volledig onder de GPL.

BASS zit **niet** in deze repository. Het script `scripts/fetch-bass.ps1` downloadt het bij de gebruiker zelf van
<https://www.un4seen.com/>. BASS is gratis voor niet-commercieel gebruik; voor commercieel gebruik is een licentie
van un4seen nodig.

## Overige onderdelen

| Onderdeel | Licentie | Gebruik |
|---|---|---|
| [TagLibSharp](https://github.com/mono/taglib-sharp) | LGPL-2.1 | Tags lezen (NuGet) |
| System.Speech (.NET) | MIT | Windows-spraak als offline stem (NuGet) |
| ASP.NET Core / .NET | MIT | Engine-service en web-UI |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | MIT | Stationsdatabase (NuGet) |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) en SQLite | Apache-2.0 en public domain | SQLite-binaries (NuGet) |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | MIT | Desktop-studio (NuGet) |
| [Dock](https://github.com/wieslawsoltes/Dock) | MIT | Dokbare en zwevende vensters (NuGet) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT | MVVM-hulpmiddelen (NuGet) |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | MIT | Opslaan van vensterindelingen (NuGet) |
| Higgs TTS 3 (Boson AI) | Onderzoek / niet-commercieel | Optioneel; draait als losse server en zit niet in deze repository |

**Let op bij Higgs TTS 3:** de modellicentie staat alleen onderzoek en niet-commercieel gebruik toe. Wie
REA-Radio-DJ commercieel wil inzetten, moet een commerciële licentie bij Boson AI regelen of een andere stem gebruiken.
Kloon nooit een stem zonder toestemming van de spreker.
