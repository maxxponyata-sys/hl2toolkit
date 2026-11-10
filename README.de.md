# HL2 Toolkit

[English](README.md) · [Русский](README.ru.md) · **Deutsch**

Ein einsteigerfreundlicher Karteneditor für **Half-Life 2**, inspiriert von den Far-Cry-Editoren. Ziehe Modelle in eine 3D-Welt, forme das Gelände mit Pinseln, drücke **F5** und spiele deine Karte.

Geschrieben in **C#** (WinForms + direktes OpenGL). Keine externen Abhängigkeiten, eine einzige ausführbare Datei für Windows x64.

---

## Funktionen

- **Drag and Drop.** Die Bibliothek enthält rund 1.400 Half-Life-2-Modelle mit texturierten Vorschaubildern, dazu NPCs, Waffen, Gegenstände, Fahrzeuge, Lichter und Wandblöcke. Einfach in die Welt ziehen oder per Doppelklick vor der Kamera platzieren.
- **Eigene Modelle.** Lege eine `.obj`-Datei im Fenster ab. Der Import-Assistent rechnet die Einheiten um, erzeugt die Textur (VTF/VMT) und die Modellquellen (SMD/QC) und kompiliert das Modell mit `studiomdl`. Danach erscheint es unter **Meine Modelle**.
- **Steuerung wie in Far Cry.** Rechte Maustaste halten und mit WASD fliegen. Objekte über den Boden ziehen oder mit Gizmos verschieben, drehen und skalieren. Rasterfang und Rückgängig/Wiederholen sind dabei.
- **Gelände-Pinsel.** Anheben, absenken, glätten, ebnen und eine zweite Texturschicht malen. Das Gelände wird als Source-Displacements exportiert.
- **Welt.** Tageszeit, Sonnenrichtung, Skybox, Nebel und Wasser.
- **Erstellen mit einem Klick.** VMF → VBSP → VVIS → VRAD → `hl2/maps`, danach startet das Spiel. Die Karte wird automatisch mit einer Skybox abgedichtet, Lecks (Leaks) sind daher ausgeschlossen.
- **RTX-Modus.** Ist Half-Life 2 RTX (oder RTX Remix im HL2-Ordner) installiert, wird die Karte dorthin kopiert und mit Pathtracing gestartet. RTX Remix kompiliert seine Shader beim ersten Start selbst.
- **VMF-Export.** Die Karte lässt sich in Hammer weiterbearbeiten.
- **Automatische Prop-Typen.** Modelle, die Half-Life 2 nur als Physikobjekte erlaubt (Fässer, Kisten …), werden automatisch erkannt.

## Voraussetzungen

- Windows 10/11 x64
- **Half-Life 2** auf Steam. Die Kompilierwerkzeuge (`vbsp`, `vvis`, `vrad`, `studiomdl`) sind im Spiel enthalten.
- Optional für den RTX-Modus: Half-Life 2 RTX und eine RTX-fähige Grafikkarte

## Download

Lade `HL2Toolkit.exe` unter [**Releases**](../../releases) herunter und starte die Datei. Keine Installation und keine .NET-Laufzeit nötig. Der Spielordner wird über Steam gefunden; falls nicht, unter **Einstellungen → Half-Life-2-Ordner** festlegen.

## Steuerung

| Aktion | Eingabe |
|---|---|
| Umsehen / fliegen | **RMT** halten + Maus, **W A S D**, **Q / E** runter/hoch, **Shift** schneller |
| Auswählen / über den Boden ziehen | **LMT** |
| Verschieben / Drehen / Skalieren | **W / E / R** |
| Auswahl / Kartenmitte fokussieren | **F** / **Pos1** |
| Auf den Boden setzen | **Ende** |
| Duplizieren / Löschen | **Strg+D** / **Entf** |
| Rückgängig / Wiederholen | **Strg+Z** / **Strg+Y** |
| Erstellen und spielen / nur erstellen | **F5** / **F7** |
| Gelände-Pinsel, umgekehrt | **LMT**, mit **Strg** umkehren |

## Aus dem Quellcode bauen

Benötigt das .NET SDK (das Projekt zielt auf `net11.0-windows`).

```bat
cd src\HL2Toolkit
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ..\..\dist
```

Kern ohne Oberfläche testen: `HL2Toolkit.exe --selftest`. Das liest Modelle, erstellt und kompiliert eine Testkarte und schreibt das Ergebnis in `selftest.log`.

## Hinweis

Ein Fan-Projekt ohne Verbindung zu Valve oder NVIDIA. Du brauchst eine eigene Kopie von Half-Life 2. Es sind keine Spieldateien enthalten.

## Lizenz

[MIT](LICENSE)
