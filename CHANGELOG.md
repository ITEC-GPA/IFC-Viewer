# Meerkat — modifiche

## 0.2.0 — 2026-11-17

- Meerkat e compilato per .NET 8 (`net8.0-windows`) e richiede Rhino 8 su .NET 8, il runtime
  .NET Core predefinito su Windows. Rhino 8 impostato su .NET Framework (`SetDotNetRuntime`)
  e Rhino 7 non caricano piu il plugin.
- `Build.ps1` esegue `dotnet build` di `src/Meerkat.csproj` (Release) al posto del compilatore
  C# 5 di .NET Framework. Riferimenti: le librerie .NET del Rhino locale (`System\netcore`
  per RhinoCommon e Newtonsoft.Json, Grasshopper e GH_IO dalla cartella del plugin), non
  copiate in bin. Serve l'SDK .NET 8 o successivo.
- Bin e distribuzione invariate nella struttura: un solo `Meerkat.gha` con gli stessi file
  accanto, manifest `distribution.json` e pacchetto ZIP come prima. Il commit di build
  (`BuildCommit`) letto dal controllo della bin resta scritto nell'assembly.
- Test Rhino: l'host Rhino.Inside per .NET 8 (`tests/RhinoHarness.csproj`) sostituisce
  l'eseguibile .NET Framework compilato con csc e viene avviato con `dotnet`. Prima di Rhino
  carica il `System.Management.dll` Windows di Rhino, per non invalidare la licenza Cloud Zoo
  del Rhino interattivo. In Grasshopper carica solo i componenti di Grasshopper e il plugin
  in prova, non la copia registrata con `Meerkat.ghlink` ne le altre librerie dell'utente.
- Nessuna modifica a componenti, GUID, parametri, worker Python ed esempi.

Verifiche eseguite con Rhino 8.37 su .NET 8: 33 test Python; suite Rhino completa
(`Test-Rhino.ps1 -All`, 12 processi completati, 927 controlli) con i 18 esempi e la
definizione legacy; `Test-BuildLayout.ps1`; pacchetto ZIP con gli stessi 164 file del
manifest. La distribuzione isolata (`Test-Distribution.ps1`) non e stata ripetuta: richiede
l'installazione di IfcOpenShell da Internet.

## 0.1.1 — 2026-11-09

- Avvio dei worker Python centralizzato: verifica dell'interprete e dello script,
  gestione uniforme di stdout/stderr UTF-8, errori e timeout con attesa della terminazione.
  Il worker ignora PYTHONHOME/PYTHONPATH ereditati da Rhino e i pacchetti del sito utente,
  evitando il conflitto fra il Python incorporato in Rhino e il runtime esterno.
- Gli archivi compressi rimangono in memoria per tutta la vita del modello.
  Salvataggio GH e archivio non dipendono dai file temporanei; le copie richieste dai
  worker vengono ricreate se mancanti e rimosse alla raccolta del modello.
  Questo richiede memoria aggiuntiva pari alla dimensione degli archivi compressi.
- Un READ fallito mantiene i modelli precedenti. READ resta sincrono.
- Il controllo del commit di build funziona anche con assembly caricate in memoria;
  l'inizializzazione attende la registrazione nel component server di Grasshopper.
- Pannelli ordinati da 1 Read a 8 Structural, senza duplicati Check.
  GUID, parametri dei 35 componenti e marcatori IFCViewer conservati.
- Setup: nuove installazioni su Python x64 3.12, poi 3.11. Interpreti espliciti
  3.9–3.12 e runtime legacy esistenti restano utilizzabili; Python 3.13 viene rifiutato
  per compatibilita con IfcOpenShell 0.8.3. Le dipendenze IFC restano alla versione 0.8.3.
- Percorso Rhino configurabile anche nei test; interprete configurabile nella prova
  della distribuzione isolata. Documentazione ed esempi aggiornati.
- Lettura del manifest corretta per Windows PowerShell 5.1 in Package.ps1 e nel
  test della distribuzione, oltre alla compatibilita con PowerShell 7.

Verifiche eseguite: 33 test Python sia su 3.9.13 sia su 3.12.10; suite Rhino 8.36
con i 18 esempi e definizioni legacy; regressioni finali su worker, persistenza,
pulizia e caricamento in memoria; distribuzione isolata con Python 3.12.10 e setup
Windows PowerShell 5.1; controlli layout, CSV e integrita/hash del pacchetto ZIP.

La prova su un IFC reale di progetto resta da eseguire: i modelli di regressione
sono sintetici e coprono IFC2x3, IFC4 e IFC4x3.
