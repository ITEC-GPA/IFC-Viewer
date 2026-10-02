# Otto esempi Grasshopper IFC — tutti i 19 componenti

Apri i file `.gh` in **Rhino 8 / Grasshopper su Windows**, dopo aver eseguito `Setup.cmd` dalla cartella principale e riavviato Rhino. Il plugin Meerkat deve essere caricato. I modelli sono gia incorporati: per provare gli esempi non occorre premere READ, ne avere Python in esecuzione. Sono disponibili anche le copie `.ghx` leggibili come XML.

Apri un esempio alla volta e usa Zoom Extents nella viewport Rhino. I pannelli azzurri spiegano il flusso o contengono input modificabili; quelli verdi mostrano i risultati. I componenti Custom Preview gestiscono i colori; gli altri componenti geometrici hanno la preview disattivata per evitare sovrapposizioni.

| File da aprire | Cosa impari | Risultato iniziale |
| --- | --- | --- |
| [01 — Abaco pareti](01_Abaco_pareti.gh) | Filtrare, ordinare, leggere parametri, esportare CSV | 3 pareti, 12 m3 |
| [02 — Piani e colori](02_Piani_e_colori.gh) | Raggruppare, colorare e fare bake dei rami | 4 oggetti al piano terra, 2 al primo |
| [03 — Controllo parametri](03_Controllo_parametri.gh) | Trovare parametri obbligatori mancanti | 2 conformi, 1 da correggere |
| [04 — Confronto revisioni](04_Confronto_revisioni.gh) | Confrontare dati e geometrie per GlobalId | 1 aggiunto, 1 rimosso, 2 modificati |
| [05 — Origine locale ed export](05_Origine_locale_export.gh) | Gestire coordinate grandi, salvare 3DM e archivio | 6 mesh centrate in XY |
| [06 — Esplora modello](06_Esplora_modello.gh) | Schema/unita, scoperta parametri e selezione per ID | 11 record, 6 mesh; 1 parete selezionata |
| [07 — Filtri numerici](07_Filtri_numerici.gh) | Slider, filtri AND, ordinamento numerico e somma volumi | 2 pareti, 9 m3 |
| [08 — Bake e rilettura](08_Bake_e_rilettura.gh) | User Text, aggiornamento senza duplicati e Refresh | 6 oggetti dopo BAKE |

Per iniziare dai dati usa **06**. Per il flusso che porta gli IFC in Rhino con layer e tutti i parametri usa **08**.

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

## 06 — Esplora modello, parametri e identificativi

File: [06_Esplora_modello.gh](06_Esplora_modello.gh)

`Read IFC -> Model Info / Property Keys / Select IDs -> IFC Elements + Property Value -> Custom Preview`

- Model Info mostra schema IFC4, 11 record, 6 mesh, classi e conteggi. La scala lineare 0,001 converte le lunghezze del modello da millimetri a metri.
- Property Keys cerca `FireRating`: restituisce le chiavi esatte e il numero di oggetti nei quali sono presenti. Le proprieta effettive, di istanza e di tipo sono distinte.
- Select IDs legge un GlobalId valido e `ID_NON_PRESENTE`: seleziona la prima parete e segnala intenzionalmente il secondo testo nel pannello ID NON TROVATI.
- Property Value mostra REI 60 e il flag di presenza; IFC Elements mostra la mesh e il JSON completo della parete.

Prova a cercare `Progetto`, poi copia una delle chiavi nel pannello PARAMETRO. Per usare altri oggetti sostituisci gli ID nel pannello: sono accettati GlobalId, `#STEP-ID` e `IFC.Key`. Un GUID Rhino prodotto dal bake non e un GlobalId IFC. Il JSON puo essere lungo: apri il pannello con doppio clic per leggerlo.

## 07 — Filtri numerici, booleani e slider

File: [07_Filtri_numerici.gh](07_Filtri_numerici.gh)

`Read IFC -> IFC Elements (IfcWall) -> Sort numerico -> Filter Property >= slider -> Filter Property Selezionabile=true`

`Selezione -> Geometry Metrics -> Mass Addition; selezione -> Custom Preview / Export CSV`

Il modello dedicato `tutorial_numeri.ifc` assegna alle tre pareti due proprieta dimostrative:

| Parete | Tutorial.Indice | Tutorial.Selezionabile |
| --- | ---: | --- |
| Parete 1 | 2 | true |
| Parete 2 | 10 | true |
| Parete P1 | 1 | false |

Sort ha Numeric=True: l'ordine e **1, 2, 10**, diverso dall'ordine alfabetico dei numeri. I due filtri in serie combinano le condizioni con AND. Con soglia **2** passano due pareti, per **9 m3**; con soglia **10** ne passa una, per **4,5 m3**. Con soglia **0** il filtro booleano esclude comunque Parete P1. Con soglia oltre 10 la selezione e vuota: non ci sono oggetti da esportare e il pannello somma puo restare vuoto.

La somma usa il componente nativo **Mass Addition** collegato ai volumi geometrici in m3. WRITE CSV esporta GlobalId, nome, indice e flag della selezione corrente. `Indice` e un numero didattico senza unita; per parametri dimensionali reali considera le unita IFC prima di confrontare valori.

## 08 — Bake, attributi e aggiornamento Rhino

File: [08_Bake_e_rilettura.gh](08_Bake_e_rilettura.gh)

`Read IFC -> Filter Property (GeometryStatus=ok) -> Bake IFC -> Read Baked -> pannelli / List Length`

1. Premi **BAKE**: vengono create sei mesh con layer `Esempio_08::sorgente::IfcClass` e tutti gli User Text IFC.
2. Consulta i pannelli: GUID Rhino, chiavi e valori per ramo, JSON completo; il componente nativo List Length mostra sei oggetti.
3. Premi nuovamente BAKE: Update=True aggiorna gli oggetti esistenti mantenendo i GUID e senza duplicarli.
4. Aggiungi o modifica un attributo User Text in Rhino e premi **REFRESH**: Read Baked rilegge il dato corrente. `IFC.MetadataJSON` conserva il record originale del bake; una modifica manuale a un singolo User Text non riscrive quel JSON.
5. Salva il documento Rhino `.3dm` per conservare geometrie, layer e attributi. Per vedere solo gli oggetti Rhino disattiva il componente Custom Preview dopo il bake.

All'apertura il conteggio e zero in un documento vuoto. Se contiene gia oggetti Meerkat con radice `Esempio_08`, Read Baked puo mostrarli prima di premere BAKE: senza ID usa il filtro Root. I campi IFC vengono aggiornati dal modello al nuovo bake, quindi salva separatamente eventuali annotazioni che devi conservare.

## Dove trovare ciascun componente

| Componente Meerkat | Esempio consigliato |
| --- | --- |
| Read IFC | 06 |
| IFC Model Info | 06 |
| IFC Elements | 06 |
| IFC Property Keys | 06 |
| IFC Property Value | 01, 06 |
| IFC Filter Property | 07 |
| IFC Select IDs | 06 |
| IFC Group By | 02 |
| IFC Sort | 01, 07 |
| IFC Color By | 02 |
| IFC Geometry Metrics | 01, 07 |
| IFC Local Origin | 05 |
| IFC Check Data | 03 |
| IFC Compare Revisions | 04 |
| Bake IFC | 08 |
| IFC Read Baked | 08 |
| Save IFC Archive | 05 |
| IFC Export CSV | 01, 03, 04, 07 |
| IFC Export 3DM | 05 |

La copertura completa, verificata sui `.gh` riaperti dal disco, e registrata in [componenti-esempi.json](componenti-esempi.json).

## Modelli e rigenerazione

Sono inclusi `tutorial_A.ifc`, `tutorial_B.ifc`, `tutorial_coordinate_grandi.ifc`, `tutorial_numeri.ifc` e i corrispondenti archivi `.ifcdata.zip`. Sono modelli sintetici didattici: due piani, pareti, solai, un pilastro e record senza geometria. Le modifiche della revisione B e le proprieta dell'esempio numerico sono scritte negli IFC sorgente.

`Build-Examples.ps1`, dalla cartella principale, rigenera i modelli con IfcOpenShell e le otto definizioni con il runtime Rhino. Richiede l'ambiente Python del progetto, Rhino 8 e una licenza disponibile. Il generatore riapre ogni `.gh` e verifica conteggi, parametri, confronto, selezione per ID, interazione dello slider, bake/update/Refresh, CSV, 3DM e archivio in documenti di prova. I file consegnati sono salvati prima di attivare i pulsanti nei test. `tests/check_examples.py` controlla inoltre le righe CSV con il parser CSV di Python.
