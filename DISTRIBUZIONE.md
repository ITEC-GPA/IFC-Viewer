# Meerkat per Grasshopper

![Meerkat](Assets/Brand/logo.png)

Lettura IFC, geometrie, attributi BIM, controlli e bake su layer per tipologia.

Questa cartella e la distribuzione completa di **Meerkat** per Rhino 8 / Grasshopper 1 su Windows x64. Mantieni insieme i suoi file; puoi rinominare o spostare la cartella prima dell'installazione. Non servono i sorgenti del repository.

1. Estrai tutti i file in una cartella locale e chiudi Rhino.
2. Esegui `Setup.cmd`: crea il runtime Python `.venv` qui accanto e registra la cartella in Grasshopper tramite `Meerkat.ghlink`. Servono Python x64 e accesso a Internet per installare IfcOpenShell 0.8.3. Il setup cerca Python 3.9; per un altro interprete compatibile usa `Setup.ps1 -Python "C:\percorso\python.exe"`.
3. Avvia Rhino 8, imposta le unita del documento e apri Grasshopper: cerca la scheda **Meerkat**.
4. Apri `Meerkat.gh` oppure uno degli [otto esempi guidati](examples/ESEMPI.md). I modelli incorporati funzionano anche senza Python; per leggere nuovi IFC serve il runtime.
5. Cambia i percorsi degli input/output prima di usare READ, WRITE o SAVE. Premi BAKE per creare mesh con layer e tutti gli attributi. Salva il `.gh` e il documento Rhino `.3dm` per conservare il lavoro.

Il [catalogo dei 19 componenti](COMPONENTI.md) descrive filtri, parametri, coordinate e salvataggi. [Logo e identita](Assets/Brand/README.md) e [catalogo icone](Assets/Icons/catalog.html) sono inclusi.

## Contenuto

- `Meerkat.gha`: plugin con icone incorporate; `ifc_reader.py`: motore di lettura.
- `Setup.cmd`, `Setup.ps1`, `requirements.txt`: installazione runtime e registrazione.
- `Meerkat.gh`, `Meerkat.ghx`, `examples/`: definizioni e modelli dimostrativi.
- `Assets/`: logo vettoriale, PNG, ICO e icone dei componenti.
- `distribution.json`: elenco e hash SHA-256 dei file della build. Non include i dati locali creati successivamente.

Il runtime Python non e preinstallato nello ZIP e non e un pacchetto offline. `Setup.ps1 -RegisterOnly` registra il plugin senza installare Python; `-RuntimeOnly` prepara solo Python. E possibile indicare un interprete esistente con un file locale `runtime.json` contenente `{"python":"C:/percorso/python.exe"}` oppure un percorso relativo alla cartella del plugin.

## Aggiornamento da IFC Viewer

I GUID del plugin e dei 19 componenti sono conservati: i vecchi `.gh` continuano ad aprirsi. Non caricare contemporaneamente `IfcViewer.gha` e `Meerkat.gha`. Il setup migra automaticamente il vecchio `IFCViewer.ghlink` quando punta al precedente `dist` dello stesso progetto; per un'altra installazione segnala il collegamento da disattivare. I marcatori tecnici `IFCViewer.Key` e `IFCViewer.Root` restano compatibili con i bake esistenti.

Geometrie come mesh triangolate, attributi IFC nelle unita originali, aree/volumi geometrici in SI. I test sono su modelli sintetici; non sostituiscono la verifica sul proprio IFC reale. Se sposti una cartella gia installata, aggiorna il collegamento nelle Libraries di Grasshopper.
