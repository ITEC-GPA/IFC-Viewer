# Cinque esempi Grasshopper IFC

Apri i file `.gh` in **Rhino 8 / Grasshopper su Windows**, dopo aver eseguito `Setup.cmd` dalla cartella principale e riavviato Rhino. Il plugin IFC Viewer deve essere caricato. I modelli sono gia incorporati: per provare gli esempi non occorre premere READ, ne avere Python in esecuzione. Sono disponibili anche le copie `.ghx` leggibili come XML.

Apri un esempio alla volta e usa Zoom Extents nella viewport Rhino. I pannelli azzurri spiegano il flusso o contengono input modificabili; quelli verdi mostrano i risultati. I componenti Custom Preview gestiscono i colori; gli altri componenti geometrici hanno la preview disattivata per evitare sovrapposizioni.

**Per usare un tuo modello:** cambia il pannello FILE IFC e premi READ. Per scrivere file, cambia la DESTINAZIONE e premi WRITE; per creare oggetti nel documento corrente premi BAKE. I pulsanti sono disattivati all'apertura. `Overwrite` parte da False: scegli un nuovo nome oppure abilitalo deliberatamente per sostituire un file esistente. I percorsi iniziali fanno riferimento al computer di creazione: aggiornali se sposti la cartella.

## 01 — Abaco delle pareti

File: [01_Abaco_pareti.gh](01_Abaco_pareti.gh)

`Read IFC -> Filter Property -> Sort -> Property Value / Geometry Metrics / IFC Elements -> Custom Preview / Export CSV`

- Filtra `IFC.Class = IfcWall` e ordina per nome.
- Legge `IFC.Property.Pset_WallCommon.FireRating`, comprese le proprieta ereditate dal tipo.
- Mostra tre pareti: resistenza al fuoco REI 60, REI 120, REI 120; volumi 4,5, 4,5 e 3 m3, per un totale di 12 m3.
- WRITE CSV crea un abaco con GlobalId, nome, classe, piano, codice e FireRating. I volumi sono nei pannelli di Grasshopper; questo CSV esporta le chiavi IFC elencate nel pannello COLONNE.

Prova a cambiare il valore del filtro da `IfcWall` a `IfcSlab` nell'input Value del componente Filter Property. Per includere anche sottoclassi come IfcWallStandardCase, usa il componente IFC Elements con piu classi esplicite oppure una selezione adeguata al modello: l'uguaglianza qui e esatta.

## 02 — Piani, colori e bake

File: [02_Piani_e_colori.gh](02_Piani_e_colori.gh)

`Read IFC -> Filter Property -> Group By / Color By -> Custom Preview / Bake IFC -> Read Baked`

- Seleziona gli oggetti con `IFC.GeometryStatus = ok`: sei mesh.
- Raggruppa per `IFC.Container.name`: Piano terra contiene quattro oggetti e Piano primo due. L'output Groups e un albero con un ramo per piano.
- Colora la preview secondo il piano e mostra la legenda e i conteggi.
- BAKE raccoglie entrambi i rami: crea sei oggetti, layer `Esempio_02::sorgente::IfcClass` e tutti i relativi User Text. Read Baked rilegge i GUID Rhino.

Il bake usa layer e colori per classe IFC; i colori per piano riguardano la preview. Con Update=True una seconda pressione aggiorna gli stessi oggetti senza duplicarli. Prova a sostituire la chiave nel pannello con `IFC.Class`: cambieranno insieme gruppi, colori e conteggi.

## 03 — Controllo dei parametri obbligatori

File: [03_Controllo_parametri.gh](03_Controllo_parametri.gh)

`Read IFC -> Filter Property -> Check Data -> IFC Elements -> Custom Preview / Export CSV`

- Controlla le tre pareti, richiedendo geometria, `IFC.Property.Progetto.Codice` e `IFC.Property.Pset_WallCommon.FireRating`.
- Due pareti sono verdi e conformi; una e rossa per il codice mancante.
- Il pannello PROBLEMI contiene la chiave assente, con un ramo per indice della selezione originale.
- WRITE CSV esporta solo la parete da correggere: il codice e una cella vuota, il FireRating ereditato e presente.

Modifica il pannello CHIAVI OBBLIGATORIE, una chiave per riga. Per scoprire le chiavi del tuo IFC puoi aggiungere IFC Property Keys all'uscita Elements del lettore.

## 04 — Confronto di due revisioni

File: [04_Confronto_revisioni.gh](04_Confronto_revisioni.gh)

`Read IFC A + Read IFC B -> Compare Revisions -> quattro preview + Export CSV`

Il modello B differisce da A per quattro oggetti:

| Esito | Quantita | Visualizzazione |
| --- | ---: | --- |
| Aggiunto | 1 parete | Verde, geometria B |
| Rimosso | 1 parete | Rosso, geometria A |
| Modificato | 1 parete + 1 pilastro | Arancio, geometria B |
| Invariato | 8 record, di cui 3 con mesh | Grigio |

La parete passa da REI 60 a REI 90 e il pilastro si sposta di 0,8 m lungo -X. Il pannello CAMPI MODIFICATI distingue la modifica del parametro dalla modifica geometrica. WRITE CSV esporta i due oggetti modificati nella loro versione B.

Sostituisci entrambi i percorsi e premi i due READ per confrontare le tue revisioni. Il confronto usa GlobalId stabili e rifiuta identificativi duplicati; non cerca corrispondenze per nome o vicinanza. La geometria viene confrontata come mesh, con tolleranza iniziale di 0,000001 m: una diversa triangolazione puo risultare modificata. Per vedere la vecchia posizione di un elemento modificato, aggiungi IFC Elements + Custom Preview all'uscita Changed Before.

## 05 — Origine locale e salvataggio

File: [05_Origine_locale_export.gh](05_Origine_locale_export.gh)

`Read IFC -> Filter Property -> Local Origin -> IFC Elements / Geometry Metrics / Export 3DM / Bake IFC`

`Read IFC (Models) -> Save IFC Archive`

- Il modello parte da coordinate intorno a X=650000 m e Y=4860000 m.
- Local Origin in modalita XY center calcola un offset di circa `(-650002,5; -4860002,15; 0)` metri, collegato a tutte le operazioni geometriche.
- La preview e centrata in XY; le quote Z restano invariate. In un documento Rhino in millimetri, la quota massima e 6200 mm.
- WRITE 3DM salva sei mesh con lo stesso offset, layer per classe e tutti gli attributi; il documento Rhino corrente resta invariato.
- BAKE crea gli stessi oggetti nel documento corrente, con radice `Esempio_05`.
- SAVE ARCHIVE conserva tutti gli 11 record, inclusi quelli senza mesh, e l'IFC originale. L'archivio mantiene le coordinate originali: l'offset appartiene alla definizione Grasshopper e viene applicato alla preview/bake/3DM.

La traslazione serve a lavorare vicino all'origine; non applica un sistema di riferimento GIS o IfcMapConversion. Per ritornare alle coordinate originali usa offset zero oppure sottrai agli oggetti locali l'offset applicato, convertendolo nelle unita Rhino.

## Modelli e rigenerazione

Sono inclusi `tutorial_A.ifc`, `tutorial_B.ifc`, `tutorial_coordinate_grandi.ifc` e i corrispondenti archivi `.ifcdata.zip`. Sono modelli sintetici didattici: due piani, pareti, solai, un pilastro e record senza geometria. Le modifiche della revisione B sono scritte nell'IFC sorgente.

`Build-Examples.ps1`, dalla cartella principale, rigenera i modelli con IfcOpenShell e le cinque definizioni con il runtime Rhino. Richiede l'ambiente Python del progetto, Rhino 8 e una licenza disponibile. Il generatore riapre ogni `.gh` e verifica conteggi, parametri, confronto, bake, CSV, 3DM e archivio in documenti di prova. I file consegnati sono salvati prima di attivare i pulsanti nei test. `tests/check_examples.py` controlla inoltre le righe CSV con il parser CSV di Python.
