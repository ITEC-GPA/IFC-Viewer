# IFC Viewer — catalogo dei 19 componenti

Tutti i componenti si trovano nella scheda **IFC Viewer** di Grasshopper. La definizione `IFC_Viewer.gh` li contiene gia collegati in cinque aree. I componenti di analisi usano gli elementi della selezione principale; le esportazioni restano inattive finche non premi un Button.

## Leggere e visualizzare

| Componente | Input principali | Output / utilizzo |
| --- | --- | --- |
| **Read IFC** | Files, Read | Models completi, Elements e Report. Legge IFC/IFCZIP/archivi. Salva i modelli nel `.gh`. |
| **IFC Model Info** | Models | Nomi, schema, numero prodotti/mesh, fattore unita, classi e conteggi per ramo `{modello}`. |
| **IFC Elements** | Elements, Classes, Search, Offset | Mesh nelle unita Rhino, elementi filtrati, classi/GlobalId, chiavi/valori e JSON. |
| **IFC Color By** | Elements, Key, Offset | Mesh e colori da collegare a Custom Preview; legenda, conteggi e indicatore di chiave presente. |
| **IFC Geometry Metrics** | Elements, Offset | Area totale in m², volume in m³, dimensioni XYZ in metri, box/centri nelle unita Rhino. |
| **IFC Local Origin** | Elements, Mode | Vettore in metri per avvicinare gli oggetti all'origine. |
| **IFC Read Baked** | Rhino object IDs, Root, Refresh | Mesh correnti nel documento, User Text e JSON IFC; lista degli ID non trovati. |

I rami `{i}` identificano gli oggetti della lista corrente. Gli oggetti senza mesh hanno rami geometrici vuoti: l'indice dei parametri resta coerente. Quando filtri o ordini cambia l'ordine della lista risultante: usa GlobalId/IFC.Key per risalire agli oggetti originali.

**Color By** applica colori alle categorie testuali; non genera una scala numerica continua. Il colore della chiave assente e grigio. I valori `false`, `0`, stringa vuota e `null` sono categorie presenti. La legenda usa `Key exists=False` per distinguere l'assenza da un eventuale valore letterale `(assente)`. Nell'esempio il secondo Custom Preview e disattivato per evitare sovrapposizioni: attivalo e disattiva la preview principale per usare i colori per piano. Il bake mantiene i colori per classe IFC.

**Geometry Metrics** misura la mesh tessellata, non il valore dichiarato nei Quantity Set. Le superfici comprendono tutte le facce, quindi non sono automaticamente superfici nette di computo. Il volume viene restituito solo per mesh chiuse/orientate; altrimenti `NaN` e `Has volume=False`. Le misure non costituiscono una verifica di solidita topologica o di assenza di autointersezioni. Area/volume mantengono le stesse unita SI anche se Rhino lavora in millimetri.

**Local Origin** supporta `XY center` (mantiene Z), `center` e `min`. L'offset proposto non viene applicato automaticamente: collega lo stesso vettore a IFC Elements/Color By e Bake/Export 3DM. Per una federazione calcolalo sull'insieme dei modelli, cosi tutti ricevono la stessa traslazione. Non modifica l'IFC sorgente e non applica conversioni GIS.

**Read Baked** usa i GUID Rhino dell'uscita Bake, non i GlobalId IFC. Se IDs e vuoto legge tutti gli oggetti marcati da IFC Viewer, con eventuale filtro Root. Dopo modifiche effettuate in Rhino premi Refresh. Il componente legge il documento attivo, quindi funziona anche dopo aver riaperto solo il `.3dm`; non ricostruisce un archivio IFC o oggetti Elements da riutilizzare nei filtri IFC. Le mesh lette si trovano nelle coordinate correnti Rhino; il JSON descrive i dati originali del bake.

## Cercare, filtrare e organizzare

| Componente | Funzione | Dettaglio |
| --- | --- | --- |
| **IFC Property Keys** | Scopre i nomi esatti dei parametri | Search filtra le chiavi; Occurrences conta gli elementi con la chiave, anche se nulla. |
| **IFC Property Value** | Estrae un parametro per ogni oggetto | Values, Found, Numbers, Is numeric. Assenti/non numerici hanno Numbers=`NaN`. |
| **IFC Filter Property** | Suddivide elementi in Matched e Rejected | Produce anche una maschera nello stesso ordine dell'input. |
| **IFC Select IDs** | Seleziona per GlobalId, `#STEP-ID` o IFC.Key | Restituisce indici originali, ID assenti e ID ambigui. |
| **IFC Group By** | Un ramo per valore del parametro | Etichette, conteggi e indicatore di chiave presente. |
| **IFC Sort** | Ordina per testo oppure numero | Ordine crescente/decrescente; assenti/non numerici in fondo. Ordinamento stabile. |

Chiavi utili:

| Dato | Key |
| --- | --- |
| Classe IFC | `IFC.Class` |
| Nome oggetto | `IFC.Name` |
| GlobalId | `IFC.GlobalId` |
| Identita univoca nella federazione | `IFC.Key` |
| File sorgente | `IFC.Source` |
| Tipo associato | `IFC.Type.Name` |
| Piano/contenitore spaziale | `IFC.Container.name` |
| Primo materiale | `IFC.Material[0].Name` |
| Resistenza al fuoco effettiva | `IFC.Property.Pset_WallCommon.FireRating` |
| Esterno/interno | `IFC.Property.Pset_WallCommon.IsExternal` |
| Lunghezza dichiarata IFC | `IFC.Property.Qto_WallBaseQuantities.Length` |

I nomi dipendono dai dati realmente presenti nell'IFC: usa Property Keys per individuarli. La chiave distingue sempre maiuscole/minuscole; nei valori del filtro puoi scegliere `Case sensitive`. Nei nomi delle chiavi, eventuali `.`, `%`, `[` e `]` appartenenti al nome IFC vengono codificati per evitare collisioni; copia il nome esatto da Property Keys.

Operatori del filtro:

| Operatore | Effetto |
| --- | --- |
| `exists`, `missing` | Controlla la presenza della chiave; `exists` include i valori nulli. |
| `=`, `!=` | Confronto testuale; le chiavi assenti non soddisfano nessuno dei due. |
| `contains`, `starts with` | Ricerca testuale. |
| `>`, `>=`, `<`, `<=` | Confronto numerico; esclude valori assenti e non numerici. |

Per i numeri usa il **punto decimale**, senza separatore delle migliaia; e ammessa la notazione scientifica. `=` confronta testo, quindi `2` e `2.0` possono differire. Per esprimere uguaglianza numerica usa due filtri `>=` e `<=`. Per condizioni AND collega i filtri in serie. Per OR unisci i rami Matched; se un oggetto compare in entrambi, evita di contarne due volte i parametri. Il bake deduplica per IFC.Key.

I parametri numerici conservano le **unita IFC originali**, comprese quelle esplicite sulle singole proprieta. Non confrontare o sommare automaticamente quantita di sorgenti con unita diverse. IFC Model Info espone il fattore di conversione delle lunghezze di progetto; non rappresenta necessariamente l'unita di ogni proprieta. Per misure geometriche uniformi usa Geometry Metrics.

I GlobalId IFC distinguono maiuscole/minuscole. In una federazione uno stesso GlobalId o `#42` puo identificare piu oggetti: Select IDs li restituisce tutti e segnala l'ambiguita. Per una selezione univoca usa IFC.Key, che incorpora l'identita della sorgente. I file sorgente hanno identita basata sul percorso; rinominarli/spostarli cambia IFC.Key.

## Controllare e confrontare

**IFC Check Data** verifica GlobalId mancanti/duplicati nella stessa sorgente, le chiavi richieste e opzionalmente la disponibilita della mesh. Se la chiave esiste ma il testo e vuoto o `null`, il requisito non e soddisfatto; `0` e `false` sono valori validi. Anche ripetere due volte lo stesso elemento nella selezione produce una segnalazione di duplicato. Il risultato consiste in Passed, Failed, maschera Valid e un ramo di Issues per ogni oggetto originale. Non e una validazione dello schema IFC, delle norme o dei requisiti IDS.

**IFC Compare Revisions** riceve un singolo modello prima e un singolo modello dopo, normalmente dalle uscite Models di due Read IFC. Gli output sono Added, Removed, Changed before, Changed after, Unchanged e Changed fields. I due output Changed mantengono lo stesso ordine; ogni ramo di Changed fields spiega quali dati sono diversi.

Il confronto:

- Abbina tramite GlobalId e rifiuta GlobalId duplicati nella stessa revisione; quelli assenti sono esclusi con un avviso.
- Confronta attributi, proprieta di istanza/tipo/effettive, unita, contenitore, materiali e associazioni conservate.
- Risolve i riferimenti presenti nei grafi delle proprieta/associazioni e ignora STEP-ID e OwnerHistory, evitando falsi cambiamenti da rinumerazione.
- Opzionalmente confronta le mesh in coordinate SI originali. Ignora l'ordine degli indici, delle facce e il verso dei triangoli; rileva spostamenti e diverse triangolazioni.
- Usa una griglia di arrotondamento (`Precision metres`, default 0.000001 m): non e una tolleranza geometrica basata sulla massima distanza tra superfici. Punti vicini a un confine della griglia possono finire in celle diverse.

Una diversa tessellazione puo essere segnalata anche se la superficie ideale e uguale. Le differenze di geometria non disponibile, relazioni inverse generiche non esposte nei metadati e contenuti non rappresentati dalle mesh non costituiscono un confronto completo di tutte le entita del file. Per conservazione integrale resta disponibile l'archivio sorgente. Il confronto non modifica i file IFC e non applica automaticamente le modifiche in Rhino.

## Salvare e fare bake

| Componente | Risultato |
| --- | --- |
| **Bake IFC** | Oggetti nel documento attivo con layer per classe e tutti gli User Text; Update evita nuove copie della stessa identita. |
| **Save IFC Archive** | Originale IFC/IFCZIP, tutte le entita, metadati e mesh, riapribili con Read IFC. |
| **IFC Export CSV** | Abaco UTF-8 con BOM, una riga per oggetto, colonne scelte tramite Keys. |
| **IFC Export 3DM** | File Rhino separato con mesh/layer/User Text della selezione, nelle unita del documento attivo. |

I quattro componenti di scrittura usano un Button e il passaggio False -> True. Per un secondo salvataggio rilascia e premi di nuovo; un Toggle lasciato True non scrive di continuo. Usa un solo percorso di destinazione per istanza di Export CSV/3DM. Bake/Export uniscono automaticamente i rami degli elementi: puoi collegare Group By mantenendo tutta la selezione. Se vuoi un file per gruppo, usa istanze separate e percorsi diversi.

CSV permette `;`, `,` o un altro singolo separatore; testo, virgolette, a capo e Unicode vengono preservati. Keys vuoto esporta tutte le chiavi tranne IFC.MetadataJSON; per esportare anche il JSON, includi esplicitamente quella chiave. `Excel safe=True` aggiunge un apostrofo alle celle che iniziano con `=`, `+`, `-` o `@` per evitare formule: anche i numeri negativi vengono cosi esportati come testo. Disattiva questa opzione quando serve il testo originale esatto e controlli la provenienza dei dati.

Le esportazioni rifiutano file esistenti finche Overwrite non e True e usano una sostituzione atomica per singolo file. Export 3DM usa un documento temporaneo e non aggiunge oggetti al documento attivo. Gli elementi senza mesh restano nel `.gh` e nell'archivio IFC, mentre nel `.3dm` non vengono creati segnaposto. I CSV non sostituiscono l'archivio completo.

## Flussi gia pronti da adattare

1. **Abaco antincendio:** Elements → Filter Property (`IFC.Class = IfcWall`) → Property Value/CSV con `IFC.Property.Pset_WallCommon.FireRating`.
2. **Suddivisione per piano:** Elements → Group By (`IFC.Container.name`); usa Labels e Counts per riepiloghi.
3. **Controllo parametri:** Elements → Check Data, Required keys = parametri obbligatori → Failed → Color By/Elements per visualizzare gli oggetti da completare.
4. **Revisione progetto:** due Read IFC → Compare Revisions → Changed after/Added → Elements per anteprima; eseguire il bake quando desiderato. Gli oggetti Removed non vengono eliminati automaticamente.
5. **Consegna Rhino:** selezione filtrata → Export 3DM e, in parallelo, Models → Save IFC Archive.
6. **Modello georeferenziato:** tutti gli Elements federati → Local Origin → stesso Offset agli strumenti di preview e di bake/export.
