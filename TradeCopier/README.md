# NinjaTrader Trade Copier (erste Version)

Diese erste Version setzt ein AddOn mit klassischer NinjaTrader-Struktur auf:

- `Addon.cs`: Lifecycle + Menüeintrag im Control Center + Account-Synchronisierung
- `Window.cs`: GUI zur Auswahl von Lead- und Follow-Konten inkl. Statusleiste
- `Engine.cs`: Kopierlogik für Entries + Flatten bei Lead-Protection

## Aktuelle Logik

1. Das **Lead-Konto** ist die Quelle.
2. Auf **Execution-Events** des Lead-Kontos werden nur **Entry-Ausführungen** (`Buy`, `SellShort`) auf Follow-Konten gespiegelt.
3. **Protection-Orders** (SL/TP) werden **nicht** auf Follow-Konten angelegt.
4. Wenn am Lead eine Protection-Order ausführt (z. B. Stop oder Target), werden die Follow-Konten auf demselben Instrument per Market-Order geflattet.
5. Die UI zeigt eine **Statusleiste** (grün/rot) für aktiv/gestoppt.
6. Die Kontolisten werden periodisch mit den im Control Center tatsächlich angezeigten Konten synchronisiert (inkl. Aus-/Einblenden je nach Control-Center-Auswahl).

## Wichtiger Hinweis

Die Implementierung ist eine robuste **Basisversion**. Je nach Broker-/NinjaTrader-Setup müssen typischerweise noch ergänzt werden:

- Teilfüllungs-Handling und Reconciliation
- Slippage-/Requote-Handling
- Session-/Connection-Recovery
- Fehler- und Retry-Logik beim Orderrouting
- Persistente Konfiguration
