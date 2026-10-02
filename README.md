# Meerkat per Grasshopper

![Meerkat](Assets/Brand/logo.png)

Progetto per **Rhino 8 / Grasshopper 1, Windows x64**, con **19 componenti**, definizione `.gh` gia collegata e un modello IFC di esempio incorporato. Compilato e verificato con Rhino 8.35. Non richiede Geometry Gym, VisualARQ o altri plugin BIM. Il motore di lettura e IfcOpenShell 0.8.3, installato in un ambiente Python locale al progetto.

## Avvio

1. Compila con `Build.ps1` oppure estrai `Meerkat_Pacchetto.zip`. La cartella **bin** nella root contiene tutto cio che va distribuito: plugin, worker, setup, asset e definizioni. Esegui il suo `Setup.cmd`: prepara il runtime accanto al plugin e registra la cartella tramite `Meerkat.ghlink`. Dal repository anche `Setup.cmd` nella root richiama quello in bin.
2. Riavvia Rhino 8, imposta le unita del documento e apri Grasshopper.
3. Apri **Meerkat.gh**. L'esempio gia incorporato contiene due pareti, un solaio, un pilastro e oggetti con soli attributi.
4. Nel pannello **FILE IFC** sostituisci il percorso con il tuo `.ifc` o `.ifczip` e premi **READ**. Puoi inserire piu percorsi, uno per riga.
5. Premi **BAKE** per creare gli oggetti Rhino con layer e User Text. Salva il documento Rhino come `.3dm`.
6. Salva il `.gh` per incorporare i dati caricati. Imposta **CARTELLA ARCHIVI** e premi **SAVE** per creare gli archivi completi `.ifcdata.zip`.
7. Riduci lo zoom per vedere le sezioni a destra e in basso: interrogazione, raggruppamento, colori, misure, controlli, revisioni ed esportazioni CSV/3DM. I pulsanti di scrittura sono inizialmente disattivati.

Meerkat e il nuovo nome di IFC Viewer. Il file e ora `bin/Meerkat.gha`, la scheda Grasshopper e **Meerkat**. I GUID del plugin e dei 19 componenti restano invariati, cosi le vecchie definizioni sono compatibili. Il setup migra il vecchio `IFCViewer.ghlink` diretto al `dist` dello stesso progetto; non caricare le due assembly contemporaneamente. I marcatori interni `IFCViewer.Key`/`IFCViewer.Root` e il namespace tecnico sono conservati per la compatibilita dei dati esistenti.

Sul computer su cui e stato creato il progetto il runtime Python e gia preparato. `Setup.cmd` puo essere rieseguito: controlla le dipendenze e registra il plugin. Su un altro computer serve Python x64; lo script seleziona Python 3.9 tramite il launcher `py`. Per scegliere un altro interprete compatibile con la wheel IfcOpenShell: `powershell -ExecutionPolicy Bypass -File Setup.ps1 -Python "C:\percorso\python.exe"`. La versione verificata qui e Python 3.9.13 x64.

## Componenti

Sono disponibili **[otto esempi guidati](examples/ESEMPI.md)** che coprono tutti i 19 componenti, ciascuno in un file `.gh` autonomo con modello incorporato: [abaco pareti](examples/01_Abaco_pareti.gh), [piani/colori/bake](examples/02_Piani_e_colori.gh), [controllo parametri](examples/03_Controllo_parametri.gh), [confronto revisioni](examples/04_Confronto_revisioni.gh), [origine locale/export](examples/05_Origine_locale_export.gh), [esplorazione e ID](examples/06_Esplora_modello.gh), [filtri numerici con slider](examples/07_Filtri_numerici.gh) e [bake/rilettura attributi](examples/08_Bake_e_rilettura.gh). I modelli IFC sorgente e le copie `.ghx` sono nella cartella `examples`. Per iniziare usa **06**; per portare gli oggetti in Rhino con layer e attributi usa **08**.

| Componente | Funzione |
| --- | --- |
| **Read IFC** | Legge uno o piu IFC/IFCZIP oppure gli archivi salvati. Restituisce modelli, tutti gli IfcProduct e un report. |
| **IFC Elements** | Filtra per classe IFC esatta o cerca nome/GlobalId; visualizza mesh; espone classe, GlobalId, chiavi, valori e JSON completo. |
| **Bake IFC** | Crea/aggiorna mesh Rhino, layer e User Text. |
| **Save IFC Archive** | Salva una copia completa e riapribile di ogni modello. |
| **IFC Model Info** | Schema, unita e conteggi per modello e classe. |
| **IFC Property Keys** | Trova le chiavi dei parametri e la loro copertura. |
| **IFC Property Value** | Estrae valori testuali/numerici e segnala quelli assenti. |
| **IFC Filter Property** | Filtra per testo, presenza o confronto numerico. |
| **IFC Select IDs** | Seleziona tramite GlobalId, STEP-ID o chiave univoca per sorgente. |
| **IFC Group By** | Raggruppa per tipo, piano, materiale o qualsiasi parametro. |
| **IFC Sort** | Ordina per testo o numero. |
| **IFC Color By** | Colori per parametro e legenda con conteggi. |
| **IFC Geometry Metrics** | Superficie, volume e ingombri delle mesh. |
| **IFC Local Origin** | Calcola un offset comune per modelli lontani dall'origine. |
| **IFC Check Data** | Controlla parametri richiesti, duplicati e mesh mancanti. |
| **IFC Compare Revisions** | Rileva aggiunte, rimozioni e modifiche tra due revisioni. |
| **IFC Export CSV** | Esporta abachi di parametri in UTF-8. |
| **IFC Export 3DM** | Salva una selezione con layer/attributi in un file Rhino separato. |
| **IFC Read Baked** | Rilegge User Text e mesh gia presenti nel documento Rhino. |

Il [catalogo dei componenti](COMPONENTI.md) contiene input/output principali, chiavi di esempio e flussi di lavoro.

Nel componente **IFC Elements**, `Classes` accetta ad esempio `IfcWall`, `IfcWallStandardCase`, `IfcSlab`, `IfcDoor`, un nome per riga. Il filtro e esatto: per includere le pareti standard occorre aggiungere anche `IfcWallStandardCase`. Lascia vuoto per includere tutto. `Search` cerca in nome, GlobalId e classe. Gli output Geometry, Keys e Values hanno un ramo `{i}` per oggetto; Geometry mantiene anche i rami vuoti degli oggetti senza mesh. I parametri vengono mantenuti come testo o JSON, senza perdere `false`, zero, valori nulli o caratteri Unicode.

READ, BAKE, SAVE e WRITE agiscono sul passaggio **False -> True**: usa i Button gia collegati. Un Toggle lasciato su True non ripete l'operazione ad ogni ricalcolo; riportalo su False prima di premere di nuovo. Una modifica al percorso non rilegge da sola il file: premi READ. Il report indica la sorgente effettivamente caricata. Bake ed esportazioni uniscono i rami degli elementi in una lista; ogni componente di scrittura va usato con un solo pulsante e una sola destinazione.

## Layer e attributi

Il bake crea una gerarchia come:

```text
IFC
  modello_abc123
    IfcWall
    IfcSlab
    IfcColumn
    IfcDoor
```

Il suffisso distingue file con nomi uguali provenienti da cartelle diverse. `Root layer` cambia il livello iniziale. Ogni classe ha un colore deterministico; nell'esempio e collegato un Custom Preview con gli stessi colori.

In Rhino, seleziona un oggetto e apri **Proprieta > Testo utente attributo / Attribute User Text**. Sono presenti:

- `IFC.Class`, `IFC.GlobalId`, `IFC.StepId`, nome, schema, sorgente e hash SHA-256.
- `IFC.Key` e `IFC.SourceKey`: identificativi per distinguere oggetti provenienti da modelli diversi.
- `IFC.Attribute.*`: tutti gli attributi diretti IFC, con riferimenti alle altre entita.
- `IFC.Type.*`: attributi del tipo associato.
- `IFC.Property.*`: Property Set e Quantity Set effettivi, con precedenza ai valori di istanza.
- `IFC.InstanceProperty.*` e `IFC.TypeProperty.*`: valori originali separati, anche se una proprieta di istanza sovrascrive quella del tipo.
- `IFC.Container.*`, `IFC.Material.*`: contenitore spaziale e materiali.
- `IFC.MetadataJSON`: struttura completa dell'oggetto, comprendente grafo delle proprieta, tipi IFC dei valori, unita esplicite, limiti/intervalli, materiali stratificati, classificazioni e altre associazioni.

Nei nomi delle chiavi, `%`, `.`, `[` e `]` presenti nei nomi IFC vengono codificati per evitare collisioni. Il JSON mantiene i nomi originali. Valori e quantita IFC conservano le **unita originarie**: solo la geometria viene scalata nelle unita Rhino. I riferimenti STEP sono risolvibili nel file `entities.jsonl` dell'archivio.

Con **Update=True**, il bake riconosce gli oggetti tramite percorso della sorgente + GlobalId, all'interno della stessa radice. Aggiorna geometria e attributi conservando il GUID Rhino; non elimina gli oggetti assenti dalla selezione corrente. Non aggiorna oggetti estranei a Meerkat. Spostare/rinominare la sorgente IFC crea una nuova identita di modello. Con **Update=False** crea nuove copie. Ogni bake e raccolto in un singolo Undo; gli oggetti bloccati/nascosti causano un errore. In caso di errore durante un batch, il report segnala che potrebbe essere necessario Undo per annullare gli oggetti gia elaborati.

## Cosa viene salvato

| File | Contenuto |
| --- | --- |
| `.gh` / `.ghx` | Definizione e archivi dei modelli caricati incorporati nel componente Read IFC. Si riaprono senza il file IFC originale e senza Python; serve il plugin `.gha`. |
| `.3dm` | Oggetti effettivamente sottoposti a bake, layer e tutti i relativi User Text. Salvare da Rhino oppure usare IFC Export 3DM per la selezione. |
| `.csv` | Abaco dei parametri selezionati; non contiene geometrie o archivio IFC. |
| `.ifcdata.zip` | IFC/IFCZIP originale byte per byte, tutte le entita IFC, metadati e mesh. Il componente Read IFC lo riapre anche senza Python. |

Ogni archivio contiene `original/source.ifc` oppure `original/source.ifczip`, `entities.jsonl`, `model.json`, `geometry/<STEP-ID>.json`. Le entita senza geometria restano nella definizione e nell'archivio: non vengono inventati oggetti Rhino vuoti. Il file IFC originale conserva anche intestazione, relazioni inverse ricostruibili e dati non esposti nei pannelli.

Gli archivi temporanei di lettura si trovano in `%TEMP%\Meerkat`. Non eliminarli mentre il documento Grasshopper e aperto: servono al salvataggio. Dopo aver salvato/chiuso le definizioni possono essere eliminati. Gli archivi esportati sono scritti tramite file temporaneo e sostituzione atomica per singolo file; `Overwrite` e disattivato inizialmente.

## Geometria, coordinate e limiti

- La geometria e costituita da **mesh triangolate**, non da solidi/Brep parametrici. Deflessione lineare del motore: 1 mm in metri SI.
- Placement IFC applicati, comprese trasformazioni degli oggetti. Le coordinate vengono lette nel sistema ingegneristico del modello: non viene applicata una trasformazione GIS tramite IfcMapConversion.
- Per modelli lontani dall'origine, collega lo stesso vettore `Offset metres` a IFC Elements e Bake IFC. Il vettore e in metri e viene sommato prima della conversione nelle unita Rhino.
- La lettura usa fino a 8 thread del motore. READ e sincrono e puo occupare Grasshopper su file grandi; gli archivi incorporati aumentano la dimensione del `.gh`. Non e stato misurato un limite pratico su modelli di grandi dimensioni.
- Le geometrie non supportate o mancanti vengono segnalate nel report, mantenendo gli attributi. Rappresentazioni solo 2D/curve senza facce non vengono sottoposte a bake.
- Sono mostrati colori per classe; non vengono ricostruiti texture, shader o materiali render IFC. I relativi dati originali rimangono conservati.
- Non e un editor o un esportatore IFC: non rigenera un IFC dalle mesh modificate. Il salvataggio contiene il file sorgente originale.
- Build destinata a Rhino 8 su Windows. Non verificata con Rhino 7 o macOS.

## Sviluppo e verifiche

`Build.ps1` compila `bin/Meerkat.gha` con il compilatore .NET Framework di Windows e le librerie Rhino locali. Incorpora logo e 19 icone e raccoglie in **bin** worker, installer, documentazione, otto esempi e asset. Il progetto IDE/.NET SDK e `src/Meerkat.csproj`: `Directory.Build.props/targets` centralizzano anche questa compilazione nella stessa bin; i file intermedi sono in `build/obj/<progetto>`. Per usare un'altra installazione: `Build.ps1 -RhinoDir "C:\Program Files\Rhino 8"`.

Prima della compilazione vengono eliminate le `bin` residue dei moduli sotto la root. Sono esclusi la bin principale, runtime/dipendenze (`.venv`, `node_modules`, `packages`), `.git`, `build`, dati di test/esportazione e repository annidati. Non vengono attraversati collegamenti o junction; un collegamento dentro una bin da cancellare blocca la pulizia. La procedura controlla i percorsi assoluti prima della cancellazione.

**Distribuzione:** consegna l'intera cartella `bin`, oppure esegui `Package.ps1` per creare `Meerkat_Pacchetto.zip` dai soli file elencati e verificati nel manifest `bin/distribution.json`. Sorgenti, build, credenziali, configurazione locale `runtime.json` e runtime Python installati non entrano nello ZIP. Il setup installa IfcOpenShell sul computer di destinazione. Se aggiorni esempi o documentazione, ricompila per aggiornare bin prima di creare il pacchetto. `bin` e un output generato e ignorato da Git.

**Identita:** [logo e varianti](Assets/Brand/README.md), [tavola](Assets/Brand/identity.png), [catalogo delle 19 icone](Assets/Icons/catalog.html). Gli SVG e PNG sono versionati; la build non richiede Node.js. Per rigenerare gli asset: `npm install --prefix tools`, poi `node tools/generate-brand.cjs`.

```powershell
.\Build.ps1
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
.\Test-Rhino.ps1 -All
.\tests\Test-BuildLayout.ps1
.\Build.ps1
.\tests\Test-Distribution.ps1
.\Package.ps1
```

I test Python creano IFC2x3, IFC4 e IFC4x3 e verificano IFCZIP, integrita originale, tutte le entita, unita/placement, precedenza delle proprieta, unita esplicite e input invalido. Il test Rhino avvia un processo separato senza interfaccia, verifica bake/update/federazione e riapertura `.3dm`, quindi genera e riapre la definizione `.gh` con tutti i 19 componenti. Verifica anche filtri, raggruppamenti, ordinamento numerico, metriche SI, mesh aperte, esportazioni CSV/3DM, prevenzione delle riscritture e confronto revisioni con STEP-ID rinumerati. Usa una licenza Rhino disponibile. Le prove sono su modelli di test creati nel progetto; manca ancora una prova su un tuo IFC reale.

`-All` esegue in processi separati la suite completa e gli otto esempi, controllando anche nomi/categorie, risorse icona e apertura di un `.gh` originale IFC Viewer. `Test-BuildLayout.ps1` esercita la pulizia in una copia di prova, comprese junction e dipendenze da preservare. `Test-Distribution.ps1` copia i soli file della distribuzione in una cartella separata, installa li Python/IfcOpenShell e verifica la lettura di un IFC usando il plugin spostato; richiede accesso ai pacchetti Python. Gli output restano in `test-output`.

Riferimenti tecnici: [geometria IfcOpenShell](https://docs.ifcopenshell.org/ifcopenshell-python/geometry_processing.html), [proprieta e tipi IFC](https://docs.ifcopenshell.org/ifcopenshell-python/code_examples.html), [Grasshopper SDK](https://developer.rhino3d.com/api/grasshopper/), [documenti Rhino senza interfaccia](https://developer.rhino3d.com/guides/rhinocommon/code-driven-file-io/).
