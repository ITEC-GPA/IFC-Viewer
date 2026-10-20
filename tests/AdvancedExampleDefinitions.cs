using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using IfcViewer;
using Newtonsoft.Json.Linq;

internal static partial class ExampleDefinitions
{
    private static WriteIfcComponent AdvancedWriter(GH_Document doc, IGH_Param model, IGH_Param changes, string name, int y)
    {
        var writer = Tool(doc, new WriteIfcComponent(), model, 1140, y);
        writer.Params.Input[1].AddSource(changes);
        writer.Params.Input[2].AddSource(Panel(doc, "NUOVO IFC | percorso modificabile", Path.Combine(Root, "exports", name + ".ifc"), 30, y + 110, 700, 60));
        Button(doc, writer, 3, "WRITE IFC", 900, y + 70);
        Out(doc, "REPORT EXPORT | nessun file finche non premi WRITE", writer.Params.Output[1], 1390, y - 50, 650, 180);
        return writer;
    }

    private static GH_Document QuantityPreview()
    {
        var doc = new GH_Document();
        Header(doc, "12 | QUANTITA, UNITA E ANTEPRIMA", "Le tre pareti hanno volume geometrico totale 12 m3. Geometry Metrics -> Set Quantity -> Preview Changes -> Property Units.\nScriviamo Volume nel set custom Meerkat_Measured: il metodo e il volume della mesh chiusa, non una quantita netta/lorda standard dedotta automaticamente.\nPreview lavora su una copia temporanea; il modello originale resta invariato. WRITE esporta solo su richiesta.");
        var read = Reader(doc, "tutorial_A");
        var walls = Filter(doc, read.Params.Output[1], "IFC.Class", "IfcWall", 640, 345);
        var metrics = Tool(doc, new GeometryMetricsComponent(), walls.Params.Output[0], 980, 345);
        var quantity = Tool(doc, new SetQuantityComponent(), walls.Params.Output[0], 650, 690);
        ToolTests.SetText(quantity, 1, "Meerkat_Measured"); ToolTests.SetText(quantity, 2, "Volume");
        quantity.Params.Input[3].AddSource(metrics.Params.Output[1]); ToolTests.SetText(quantity, 5, "m3");
        quantity.Params.Input[6].AddSource(Panel(doc, "METODO DI MISURA", "Volume geometrico della mesh chiusa in SI", 30, 640, 490, 65));
        var preview = Tool(doc, new PreviewChangesComponent(), read.Params.Output[0], 1100, 690);
        preview.Params.Input[1].AddSource(quantity.Params.Output[0]); ToolTests.SetBool(preview, 2, true);
        Out(doc, "PRIMA / DOPO | richieste realmente applicate alla copia", preview.Params.Output[4], 1380, 520, 690, 250);
        var units = Tool(doc, new PropertyUnitsComponent(), preview.Params.Output[3], 650, 1040);
        ToolTests.SetText(units, 1, "Meerkat_Measured"); ToolTests.SetText(units, 2, "Volume"); ToolTests.SetText(units, 3, "m3");
        Out(doc, "QUANTITA RILETTE | 4.5, 4.5, 3 m3", units.Params.Output[0], 890, 940, 400, 130);
        var sum = Native(doc, "Mass Addition", units.Params.Output[0], 1480, 1040);
        Out(doc, "TOTALE ATTESO | 12 m3", sum.Params.Output[0], 1720, 940, 350, 100);
        AdvancedWriter(doc, read.Params.Output[0], quantity.Params.Output[0], "12_quantita", 1410);
        return doc;
    }

    private static GH_Document RelationsMaterials()
    {
        var doc = new GH_Document();
        Header(doc, "13 | RELAZIONI E MATERIALI", "Seleziona le pareti e naviga le relazioni esplicite verso i tipi. Un ramo per ogni parete di ingresso.\nMaterials & Layers legge assegnazioni di istanza o ereditate: nomi, strati, spessori in metri e profili con metadati originali.\nCambia Relation in Container, Parent, Parts, Groups, Openings o Connections; Recursive segue la stessa relazione senza cicli.");
        var read = Reader(doc, "tutorial_A");
        var walls = Filter(doc, read.Params.Output[1], "IFC.Class", "IfcWall", 650, 345);
        var relation = Tool(doc, new RelationshipsComponent(), walls.Params.Output[0], 1050, 345);
        ToolTests.SetText(relation, 1, "Type");
        Out(doc, "TIPI CORRELATI | i rami mantengono l'origine", relation.Params.Output[0], 1340, 260, 680, 150);
        var material = Tool(doc, new MaterialsLayersComponent(), walls.Params.Output[0], 650, 760);
        Out(doc, "MATERIALI | ordine degli strati/componenti", material.Params.Output[0], 940, 650, 500, 150);
        Out(doc, "SPESSORI IN METRI | NaN se non e uno strato", material.Params.Output[1], 1510, 650, 500, 150);
        Out(doc, "DETTAGLI | scope, offset, proprieta e profili", material.Params.Output[4], 940, 900, 1080, 280);
        FixedPreview(doc, walls.Params.Output[0], Color.Teal, 310, 1200, "PARETI");
        return doc;
    }

    private static GH_Document ImportSchedule()
    {
        var doc = new GH_Document();
        Header(doc, "14 | IMPORTAZIONE CSV", "La tabella UTF-8 aggiorna Description e FireRating delle tre pareti tramite IFC.GlobalId. Il testo CSV e incorporato nel pannello.\nImport Table valida le colonne scelte, identita, tipi e conflitti; un errore blocca tutto il batch. I valori vuoti sono testo vuoto, non cancellazioni.\nPuoi sostituire CSV con il percorso 14_aggiornamento.csv. Preview mostra la copia modificata; WRITE salva un nuovo IFC.");
        var read = Reader(doc, "tutorial_A");
        var import = Tool(doc, new ImportTableComponent(), read.Params.Output[0], 850, 450);
        var csv = Panel(doc, "CSV | testo multilinea modificabile", File.ReadAllText(Path.Combine(Root, "examples", "14_aggiornamento.csv")), 30, 540, 720, 240);
        csv.Properties.Multiline = true; import.Params.Input[1].AddSource(csv);
        ToolTests.SetText(import, 2, "IFC.Attribute.Description", Fire); ToolTests.SetBool(import, 4, true);
        Out(doc, "VALIDAZIONE RIGHE", import.Params.Output[2], 1150, 310, 920, 260);
        var preview = Tool(doc, new PreviewChangesComponent(), read.Params.Output[0], 850, 950);
        preview.Params.Input[1].AddSource(import.Params.Output[0]); ToolTests.SetBool(preview, 2, true);
        Out(doc, "DIFFERENZE EFFETTIVE", preview.Params.Output[4], 1150, 810, 920, 300);
        AdvancedWriter(doc, read.Params.Output[0], import.Params.Output[0], "14_tabella", 1360);
        return doc;
    }

    private static GH_Document ValidateRequirements()
    {
        var doc = new GH_Document();
        Header(doc, "15 | VALIDAZIONE IDS", "Il file 15_requisiti.ids richiede Codice e FireRating per le pareti. Attesi: 2 pareti conformi, 1 senza codice; esito globale False.\nI requisiti provengono dall'IDS, non sono controlli strutturali o normativi. Il report include motivazioni e specifiche non soddisfatte.\nIl percorso relativo viene cercato accanto al GH e negli examples della distribuzione. Sostituiscilo con il tuo IDS.");
        var read = Reader(doc, "tutorial_A");
        var ids = Tool(doc, new ValidateIdsComponent(), read.Params.Output[0], 850, 345);
        ids.Params.Input[1].AddSource(Panel(doc, "REQUISITI IDS", "15_requisiti.ids", 30, 550, 630, 65)); ToolTests.SetBool(ids, 2, true);
        Out(doc, "ERRORI | parete priva del codice", ids.Params.Output[3], 1170, 260, 900, 230);
        FixedPreview(doc, ids.Params.Output[0], Color.ForestGreen, 650, 780, "CONFORMI");
        FixedPreview(doc, ids.Params.Output[1], Color.IndianRed, 650, 1160, "DA CORREGGERE");
        Out(doc, "REPORT IDS COMPLETO", ids.Params.Output[4], 1170, 730, 900, 570);
        return doc;
    }

    private static GH_Document TypesAndExtract()
    {
        var doc = new GH_Document();
        Header(doc, "16 | TIPI ED ESTRAZIONE", "Duplica il tipo corrente per le tre pareti selezionate, con Pset indipendenti. Le istanze dello stesso tipo sorgente condividono la nuova copia.\nPreview elenca i valori ereditati prima/dopo. Assign Type va applicato in un passaggio separato da altre modifiche sugli stessi oggetti/tipi.\nEXTRACT esporta le pareti della copia e include antenati, materiali e tipi necessari. Nessun export all'apertura.");
        var read = Reader(doc, "tutorial_A");
        var walls = Filter(doc, read.Params.Output[1], "IFC.Class", "IfcWall", 650, 345);
        var assign = Tool(doc, new AssignTypeComponent(), walls.Params.Output[0], 1020, 345);
        ToolTests.SetBool(assign, 2, true); ToolTests.SetText(assign, 3, "Pareti - Variante Meerkat");
        var preview = Tool(doc, new PreviewChangesComponent(), read.Params.Output[0], 650, 790);
        preview.Params.Input[1].AddSource(assign.Params.Output[0]); ToolTests.SetBool(preview, 2, true);
        Out(doc, "TIPI E PROPRIETA PRIMA / DOPO", preview.Params.Output[4], 1030, 640, 1030, 300);
        var extract = Tool(doc, new ExtractModelComponent(), preview.Params.Output[3], 650, 1210);
        extract.Params.Input[1].AddSource(Panel(doc, "NUOVO IFC PARZIALE", Path.Combine(Root, "exports", "16_selezione.ifc"), 30, 1390, 750, 65));
        Button(doc, extract, 3, "EXTRACT", 430, 1300);
        Out(doc, "DIPENDENZE INCLUSE", extract.Params.Output[1], 1030, 1080, 1030, 290);
        return doc;
    }

    private static GH_Document ClashExample()
    {
        var doc = new GH_Document();
        Header(doc, "17 | INTERFERENZE E DISTANZE", "Collision con Include touching=True individua i 2 contatti fra pareti e solaio del modello demo.\nIntersection cerca penetrazioni oltre tolleranza; Clearance cerca superfici piu vicine di Distance metres. Le coordinate dei modelli devono essere gia coerenti.\nEscludi classi o coppie IFC.Key|IFC.Key. I punti sono indicativi e Distance dipende da Kind; non e un volume di intersezione.");
        var read = Reader(doc, "demo");
        var walls = Filter(doc, read.Params.Output[1], "IFC.Class", "IfcWall", 650, 345);
        var slabs = Filter(doc, read.Params.Output[1], "IFC.Class", "IfcSlab", 650, 720);
        var clash = Tool(doc, new ClashComponent(), walls.Params.Output[0], 1080, 550);
        clash.Params.Input[1].AddSource(slabs.Params.Output[0]); ToolTests.SetText(clash, 2, "Collision");
        ToolTests.SetBool(clash, 4, true); ToolTests.SetBool(clash, 8, true); clash.Hidden = false;
        Out(doc, "COPPIE | due elementi in ogni ramo", clash.Params.Output[0], 1420, 350, 620, 200);
        Out(doc, "TIPO DI INTERFERENZA", clash.Params.Output[4], 1420, 650, 620, 100);
        Out(doc, "REPORT | inclusi oggetti non verificabili", clash.Params.Output[5], 980, 960, 1060, 330);
        FixedPreview(doc, read.Params.Output[1], Color.FromArgb(100, 100, 140, 150), 280, 1130, "MODELLO");
        return doc;
    }

    private static GH_Document StructuralExample()
    {
        var doc = new GH_Document();
        Header(doc, "18 | MODELLO STRUTTURALE ANALITICO", "IFC di prova: trave rettilinea di 5 m a quota 3 m, due connessioni, una cerniera e un carrello. Sul nodo 2 e applicato Fz=-10000 N in coordinate globali.\nIl componente legge entita analitiche IFC realmente presenti; non ricostruisce un FEM dalle mesh e non verifica stabilita o correttezza dello schema.\nAssi/punti sono nelle unita Rhino; carichi e rigidezze mantengono unita e sistemi locali/globali IFC, indicati nel report.");
        var read = Reader(doc, "tutorial_struttura");
        var structural = Tool(doc, new StructuralModelComponent(), read.Params.Output[0], 850, 345); structural.Hidden = false;
        Out(doc, "MEMBRO E CONNESSIONI | GlobalId", structural.Params.Output[0], 1180, 260, 850, 150);
        Out(doc, "VINCOLI | TRUE fisso, FALSE libero, null non specificato", structural.Params.Output[3], 30, 640, 950, 300);
        Out(doc, "CARICHI | Fz -10000 N", structural.Params.Output[4], 1080, 640, 950, 300);
        Out(doc, "MODELLO, GRUPPI, UNITA E GRAFI IFC", structural.Params.Output[5], 30, 1060, 2000, 420);
        return doc;
    }

    private static void VerifyAdvanced(GH_Document doc, int number)
    {
        if (number == 12)
        {
            Check(Math.Abs(Numbers(Find<PropertyUnitsComponent>(doc), 0).Sum() - 12.0) < 1e-6, "12 | quantita realmente scritte, rilette e convertite: 12 m3");
            var original = (IfcModel)((GH_ObjectWrapper)Find<ReadIfcComponent>(doc).Params.Output[0].VolatileData.AllData(true).Single()).Value;
            var preview = (IfcModel)((GH_ObjectWrapper)Find<PreviewChangesComponent>(doc).Params.Output[0].VolatileData.AllData(true).Single()).Value;
            Check(preview.SourceKey == original.SourceKey && File.Exists(preview.ArchivePath), "12 | anteprima mantiene la protezione del percorso sorgente");
        }
        if (number == 13)
            Check(Elements(Find<RelationshipsComponent>(doc), 0).Length == 3 && Text(Find<MaterialsLayersComponent>(doc), 0).All(n => n == "Calcestruzzo"), "13 | tipi e materiali ereditati dalle tre pareti");
        if (number == 14)
            Check(Find<ImportTableComponent>(doc).Params.Output[0].VolatileDataCount == 6 && Elements(Find<PreviewChangesComponent>(doc), 3).All(e => e.UserText()[Fire] == "REI 90"), "14 | sei modifiche CSV applicate alla copia");
        if (number == 15)
            Check(Elements(Find<ValidateIdsComponent>(doc), 0).Length == 2 && Elements(Find<ValidateIdsComponent>(doc), 1).Length == 1, "15 | IDS: due conformi, una parete senza codice");
        if (number == 16)
            Check(Elements(Find<PreviewChangesComponent>(doc), 3).All(e => (string)e.Record["type_object"]["Name"] == "Pareti - Variante Meerkat"), "16 | tipo duplicato e riassegnato alle pareti");
        if (number == 17)
            Check(Find<ClashComponent>(doc).Params.Output[0].VolatileDataCount == 4, "17 | due coppie parete-solaio");
        if (number == 18)
        {
            var component = Find<StructuralModelComponent>(doc);
            var lines = component.Params.Output[1].VolatileData.AllData(true).Cast<GH_Line>().ToArray();
            Check(Text(component, 0).Length == 3 && lines.Length == 1 && Math.Abs(lines[0].Value.Length - 5000.0) < 1e-6, "18 | membro analitico 5000 mm e due connessioni");
            Check(Text(component, 4).Any(s => s.Contains("-10000")), "18 | carico IFC originale -10000 N letto");
        }
    }

    private static void ExerciseAdvanced(GH_Document doc, int number)
    {
        if (number == 12 || number == 14)
        {
            var writer = Find<WriteIfcComponent>(doc); string path = Path.Combine(OutputRoot, number + "_advanced.ifc");
            Disconnect(writer, 2); ToolTests.SetText(writer, 2, path); Disconnect(writer, 3); ToolTests.SetBool(writer, 3, true);
            Console.WriteLine("EXERCISE_WRITE_START=" + number);
            doc.NewSolution(false, GH_SolutionMode.Silent); NoErrors(doc, "WRITE " + number);
            var result = ReadExportData(path);
            Check((int)result["products"] == 11, number + " | export riaperto da IfcOpenShell: modello completo");
            if (number == 12) Check(Math.Abs((double)result["volume"] - 12.0) < 1e-6, "12 | Qto persistite nel file finale");
            else Check(result["descriptions"].Values<string>().All(s => s == "Verificato da tabella"), "14 | modifiche CSV persistite");
        }
        if (number == 16)
        {
            var extract = Find<ExtractModelComponent>(doc); string path = Path.Combine(OutputRoot, "16_subset.ifc");
            Disconnect(extract, 1); ToolTests.SetText(extract, 1, path); Disconnect(extract, 3); ToolTests.SetBool(extract, 3, true);
            doc.NewSolution(false, GH_SolutionMode.Silent); NoErrors(doc, "EXTRACT 16"); var result = ReadExportData(path);
            Check((int)result["walls"] == 3 && (int)result["slabs_columns"] == 0, "16 | IFC parziale conserva tre pareti, tipi e antenati");
        }
        if (number == 17)
        {
            var clash = Find<ClashComponent>(doc); ToolTests.SetText(clash, 5, "IfcWall"); doc.NewSolution(true, GH_SolutionMode.Silent); NoErrors(doc, "ESCLUSIONI 17");
            Check(clash.Params.Output[0].VolatileDataCount == 0, "17 | esclusione classe elimina le coppie");
        }
    }
    private static JObject ReadExportData(string path)
    {
        string script = "import sys,json,ifcopenshell; import ifcopenshell.util.element as e; m=ifcopenshell.open(sys.argv[1]); w=m.by_type('IfcWall'); print(json.dumps({'products':len(m.by_type('IfcProduct')),'walls':len(w),'slabs_columns':len(m.by_type('IfcSlab'))+len(m.by_type('IfcColumn')),'volume':sum(e.get_psets(x).get('Meerkat_Measured',{}).get('Volume',0) for x in w),'descriptions':[x.Description for x in w]}))";
        var start = new ProcessStartInfo(Engine.PythonPath(), "-c \"" + script + "\" \"" + path + "\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(start))
        {
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000)) { process.Kill(); throw new TimeoutException("Verifica IFC esportato scaduta."); }
            if (process.ExitCode != 0) throw new Exception(stderr.Result);
            return JObject.Parse(stdout.Result);
        }
    }
}
