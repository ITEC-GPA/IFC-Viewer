using System;
using System.Collections.Generic;
using System.Drawing;
using Point = System.Drawing.Point;
using System.IO;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using IfcViewer;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.Geometry;

// Executable examples: generate native definitions, reopen them and exercise
// the saved wiring against known IFC models. Writes only to the test directory.
internal static class ExampleDefinitions
{
    private const string Fire = "IFC.Property.Pset_WallCommon.FireRating";
    private const string Code = "IFC.Property.Progetto.Codice";
    private const string Floor = "IFC.Container.name";
    private const string IndexKey = "IFC.Property.Tutorial.Indice";
    private const string EnabledKey = "IFC.Property.Tutorial.Selezionabile";
    private static string Root;
    private static string OutputRoot;
    private static Action<bool, string> Check;
    private static JObject Expected;

    private static T Place<T>(GH_Document doc, T item, int x, int y) where T : IGH_DocumentObject
    { item.CreateAttributes(); item.Attributes.Pivot = new PointF(x, y); doc.AddObject(item, false); return item; }
    private static GH_Panel Panel(GH_Document doc, string title, string text, int x, int y, int w = 360, int h = 95)
    {
        var panel = Place(doc, new GH_Panel(), x, y); panel.NickName = title; panel.UserText = text;
        panel.Attributes.Bounds = new RectangleF(x, y, w, h);
        panel.Properties.Colour = Color.FromArgb(235, 242, 247);
        panel.Properties.Multiline = false;
        return panel;
    }
    private static void Note(GH_Document doc, string title, string text, int x, int y, int w, int h)
    { var p = Panel(doc, title, text, x, y, w, h); p.Properties.Multiline = true; }
    private static T Tool<T>(GH_Document doc, T c, IGH_Param source, int x, int y, string label = null) where T : GH_Component
    {
        Place(doc, c, x, y); c.Hidden = true;
        if (source != null) c.Params.Input[0].AddSource(source);
        if (label != null) c.NickName = label;
        Note(doc, c.Name, label ?? c.NickName, x - 115, y - 95, 250, 48);
        return c;
    }
    private static GH_Panel Out(GH_Document doc, string title, IGH_Param data, int x, int y, int w = 350, int h = 145)
    {
        var panel = Panel(doc, title, "", x, y, w, h); panel.AddSource(data);
        panel.Properties.Colour = Color.FromArgb(236, 246, 224); return panel;
    }
    private static void Button(GH_Document doc, GH_Component component, int input, string name, int x, int y)
    { var button = Place(doc, new GH_ButtonObject(), x, y); button.NickName = name; component.Params.Input[input].AddSource(button); }
    private static void Header(GH_Document doc, string title, string instructions)
    { Note(doc, title, instructions, 30, 20, 1870, 110); }
    private static ReadIfcComponent Reader(GH_Document doc, string modelName, int x = 300, int y = 345, string label = "MODELLO A")
    {
        var reader = new ReadIfcComponent();
        var chunk = new GH_LooseChunk("Embedded"); reader.Write(chunk);
        chunk.RemoveItem("ModelCount"); chunk.SetInt32("ModelCount", 1);
        chunk.SetByteArray("Archive0", File.ReadAllBytes(Path.Combine(Root, "examples", modelName + ".ifcdata.zip")));
        reader.Read(chunk);
        Tool(doc, reader, null, x, y, label);
        var path = Panel(doc, "FILE IFC | cambia percorso, poi premi READ", Path.Combine(Root, "examples", modelName + ".ifc"), x - 260, y - 180, 490, 58);
        reader.Params.Input[0].AddSource(path);
        Button(doc, reader, 1, "READ", x - 200, y + 25);
        return reader;
    }
    private static FilterPropertyComponent Filter(GH_Document doc, IGH_Param data, string key, string value, int x, int y)
    {
        var filter = Tool(doc, new FilterPropertyComponent(), data, x, y, value);
        ToolTests.SetText(filter, 1, key); ToolTests.SetText(filter, 3, value);
        return filter;
    }
    private static void Preview(GH_Document doc, IGH_Param geometry, IGH_Param colors, int x, int y, string name)
    {
        var proxy = Instances.ComponentServer.ObjectProxies.First(p => p.Desc.Name == "Custom Preview");
        var preview = Tool(doc, (GH_Component)proxy.CreateInstance(), geometry, x, y, name);
        preview.Params.Input[1].AddSource(colors); preview.Hidden = false;
    }
    private static GH_Component Native(GH_Document doc, string name, IGH_Param input, int x, int y)
    {
        var proxy = Instances.ComponentServer.ObjectProxies.First(p => p.Desc.Name == name && p.Desc.Category != "IFC Viewer");
        return Tool(doc, (GH_Component)proxy.CreateInstance(), input, x, y);
    }
    private static void FixedPreview(GH_Document doc, IGH_Param elements, Color color, int x, int y, string name)
    {
        var inspect = Tool(doc, new InspectIfcComponent(), elements, x, y, name);
        var swatch = Place(doc, new Param_Colour(), x + 120, y + 80);
        swatch.PersistentData.Append(new GH_Colour(color));
        Preview(doc, inspect.Params.Output[0], swatch, x + 350, y, name);
    }
    private static ExportCsvComponent Csv(GH_Document doc, IGH_Param elements, string filename, int x, int y)
    {
        var csv = Tool(doc, new ExportCsvComponent(), elements, x, y);
        var keys = Panel(doc, "COLONNE CSV | una chiave per riga", string.Join("\n", new[] { "IFC.GlobalId", "IFC.Name", "IFC.Class", Floor, Code, Fire }), x - 600, y - 150, 460, 155);
        csv.Params.Input[1].AddSource(keys);
        var path = Panel(doc, "DESTINAZIONE CSV | modificabile", Path.Combine(Root, "exports", filename + ".csv"), x - 600, y + 60, 510, 65);
        csv.Params.Input[2].AddSource(path); Button(doc, csv, 3, "WRITE CSV", x - 80, y + 110);
        Out(doc, "FILE SALVATO | vuoto finche non premi WRITE", csv.Params.Output[0], x + 200, y - 50, 650, 95);
        return csv;
    }

    private static GH_Document Schedule()
    {
        var doc = new GH_Document();
        Header(doc, "01 | ABACO PARETI", "Leggi -> filtra IfcWall -> ordina -> leggi proprieta e misure -> esporta CSV.\nModello gia incorporato: 3 pareti, volume totale 12 m3. Il CSV contiene attributi IFC, mentre le misure sono mostrate nei pannelli.\nModifica il percorso solo per usare un altro IFC. WRITE CSV salva su richiesta; Overwrite inizialmente False.");
        var read = Reader(doc, "tutorial_A");
        var filter = Filter(doc, read.Params.Output[1], "IFC.Class", "IfcWall", 650, 345);
        var sort = Tool(doc, new SortElementsComponent(), filter.Params.Output[0], 1020, 345);
        var inspect = Tool(doc, new InspectIfcComponent(), sort.Params.Output[0], 1400, 345);
        Preview(doc, inspect.Params.Output[0], inspect.Params.Output[7], 1790, 345, "PARETI");
        var key = Panel(doc, "PARAMETRO | chiave modificabile", Fire, 30, 530, 500, 65);
        var value = Tool(doc, new PropertyValueComponent(), sort.Params.Output[0], 650, 670);
        value.Params.Input[1].AddSource(key);
        Out(doc, "RESISTENZA AL FUOCO | ordine dei nomi", value.Params.Output[0], 850, 600, 450);
        var metrics = Tool(doc, new GeometryMetricsComponent(), sort.Params.Output[0], 1450, 670);
        Out(doc, "VOLUMI GEOMETRICI | m3", metrics.Params.Output[1], 1650, 600, 350);
        Out(doc, "PARETI ORDINATE", sort.Params.Output[0], 30, 735, 600, 165);
        Csv(doc, sort.Params.Output[0], "01_abaco_pareti", 850, 1110);
        return doc;
    }
    private static GH_Document Floors()
    {
        var doc = new GH_Document();
        Header(doc, "02 | PIANI, COLORI E BAKE", "Leggi -> seleziona gli oggetti con mesh -> raggruppa per piano e colora la preview -> bake.\nAttesi: Piano terra 4 oggetti, Piano primo 2 oggetti. Il filtro esclude i contenitori spaziali senza mesh.\nBAKE crea 6 mesh con tutti i parametri e layer per classe IFC. I colori per piano valgono per la preview; i colori del bake seguono la classe IFC.");
        var read = Reader(doc, "tutorial_A");
        var filter = Filter(doc, read.Params.Output[1], "IFC.GeometryStatus", "ok", 650, 345);
        var key = Panel(doc, "RAGGRUPPA E COLORA PER", Floor, 30, 520, 470, 65);
        var group = Tool(doc, new GroupByComponent(), filter.Params.Output[0], 650, 720);
        group.Params.Input[1].AddSource(key);
        Out(doc, "PIANI | un ramo per etichetta", group.Params.Output[1], 900, 610);
        Out(doc, "OGGETTI PER PIANO", group.Params.Output[2], 1280, 610);
        var color = Tool(doc, new ColorByComponent(), filter.Params.Output[0], 1060, 345);
        color.Params.Input[1].AddSource(key);
        Preview(doc, color.Params.Output[0], color.Params.Output[1], 1430, 345, "COLORI PER PIANO");
        Out(doc, "LEGENDA | stesso ordine dei colori C", color.Params.Output[2], 1670, 275, 340);
        var bake = Tool(doc, new BakeIfcComponent(), group.Params.Output[0], 650, 1080);
        ToolTests.SetText(bake, 2, "Esempio_02"); Button(doc, bake, 1, "BAKE", 420, 1120);
        Out(doc, "BAKE | layer Esempio_02::sorgente::IfcClass", bake.Params.Output[1], 890, 980, 670);
        var baked = Tool(doc, new ReadBakedComponent(), bake.Params.Output[0], 1680, 1080);
        ToolTests.SetText(baked, 1, "Esempio_02");
        Out(doc, "GUID RHINO DEGLI OGGETTI CREATI", baked.Params.Output[0], 890, 1170, 670);
        return doc;
    }
    private static GH_Document Quality()
    {
        var doc = new GH_Document();
        Header(doc, "03 | CONTROLLO PARAMETRI", "Leggi -> filtra le pareti -> controlla Codice e FireRating -> evidenzia gli errori -> esporta le sole pareti da correggere.\nVerde: 2 pareti complete. Rosso: 1 parete senza Progetto.Codice. La geometria e richiesta.\nLe chiavi nel pannello possono essere sostituite con i parametri obbligatori del tuo progetto.");
        var read = Reader(doc, "tutorial_A");
        var filter = Filter(doc, read.Params.Output[1], "IFC.Class", "IfcWall", 650, 345);
        var check = Tool(doc, new ValidateElementsComponent(), filter.Params.Output[0], 1060, 345);
        ToolTests.SetBool(check, 2, true);
        var keys = Panel(doc, "CHIAVI OBBLIGATORIE | una per riga", Code + "\n" + Fire, 30, 530, 590, 95);
        check.Params.Input[1].AddSource(keys);
        Out(doc, "PROBLEMI | ramo = indice della parete", check.Params.Output[3], 1390, 255, 610, 180);
        FixedPreview(doc, check.Params.Output[0], Color.ForestGreen, 940, 700, "CONFORMI | verde");
        FixedPreview(doc, check.Params.Output[1], Color.Crimson, 1550, 700, "DA CORREGGERE | rosso");
        Out(doc, "PARETE CON CODICE ASSENTE", check.Params.Output[1], 30, 710, 640, 155);
        Csv(doc, check.Params.Output[1], "03_parametri_mancanti", 850, 1110);
        return doc;
    }
    private static GH_Document Revision()
    {
        var doc = new GH_Document();
        Header(doc, "04 | CONFRONTO REVISIONI A / B", "Due modelli incorporati -> confronto per GlobalId -> preview degli esiti e CSV degli oggetti modificati.\nB contiene: 1 parete aggiunta (verde), 1 rimossa (rosso), 2 oggetti modificati (arancio), 8 record invariati (grigio; 3 hanno mesh).\nLe modifiche sono FireRating REI 60 -> REI 90 e lo spostamento di un pilastro. I rimossi usano la geometria di A, gli altri quella di B.");
        var a = Reader(doc, "tutorial_A", 300, 345, "PRIMA | A");
        var b = Reader(doc, "tutorial_B", 300, 670, "DOPO | B");
        var diff = Tool(doc, new CompareRevisionsComponent(), a.Params.Output[0], 690, 510);
        diff.Params.Input[1].AddSource(b.Params.Output[0]);
        Out(doc, "REPORT CONFRONTO", diff.Params.Output[6], 930, 165, 1080, 90);
        Out(doc, "CAMPI MODIFICATI | rami allineati a Changed After", diff.Params.Output[5], 30, 805, 770, 170);
        FixedPreview(doc, diff.Params.Output[0], Color.ForestGreen, 1080, 400, "AGGIUNTI | verde");
        FixedPreview(doc, diff.Params.Output[1], Color.Crimson, 1700, 400, "RIMOSSI | rosso");
        FixedPreview(doc, diff.Params.Output[3], Color.DarkOrange, 1080, 730, "MODIFICATI | arancio");
        FixedPreview(doc, diff.Params.Output[4], Color.LightGray, 1700, 730, "INVARIATI | grigio");
        Csv(doc, diff.Params.Output[3], "04_oggetti_modificati", 900, 1170);
        return doc;
    }
    private static GH_Document LocalOrigin()
    {
        var doc = new GH_Document();
        Header(doc, "05 | ORIGINE LOCALE, EXPORT 3DM E ARCHIVIO", "Coordinate di esempio intorno a X=650000 m, Y=4860000 m -> offset XY comune a preview, bake, misure ed export 3DM.\nLa quota Z resta invariata. Il 3DM contiene 6 mesh con layer e User Text; SAVE ARCHIVE conserva anche i record senza geometria e l'IFC originale.\nIl cambio origine e una traslazione del modello per Rhino; non effettua trasformazioni GIS. Modifica le destinazioni prima di esportare.");
        var read = Reader(doc, "tutorial_coordinate_grandi");
        var filter = Filter(doc, read.Params.Output[1], "IFC.GeometryStatus", "ok", 650, 345);
        var origin = Tool(doc, new OriginComponent(), filter.Params.Output[0], 1030, 345);
        Out(doc, "OFFSET XY | metri", origin.Params.Output[0], 1260, 260, 530, 115);
        var inspect = Tool(doc, new InspectIfcComponent(), filter.Params.Output[0], 1030, 655);
        inspect.Params.Input[3].AddSource(origin.Params.Output[0]);
        Preview(doc, inspect.Params.Output[0], inspect.Params.Output[7], 1390, 655, "PREVIEW LOCALE");
        var metrics = Tool(doc, new GeometryMetricsComponent(), filter.Params.Output[0], 1790, 655);
        metrics.Params.Input[1].AddSource(origin.Params.Output[0]);
        Out(doc, "CENTRI NELLE UNITA RHINO", metrics.Params.Output[5], 1590, 770, 470, 160);
        var export = Tool(doc, new Export3dmComponent(), filter.Params.Output[0], 650, 1070);
        export.Params.Input[4].AddSource(origin.Params.Output[0]); ToolTests.SetText(export, 5, "Esempio_05");
        var path = Panel(doc, "DESTINAZIONE 3DM", Path.Combine(Root, "exports", "05_modello_locale.3dm"), 30, 790, 600, 60);
        export.Params.Input[1].AddSource(path); Button(doc, export, 2, "WRITE 3DM", 360, 1120);
        Out(doc, "REPORT EXPORT 3DM", export.Params.Output[1], 870, 995, 650, 140);
        var bake = Tool(doc, new BakeIfcComponent(), filter.Params.Output[0], 1790, 1110);
        bake.Params.Input[4].AddSource(origin.Params.Output[0]); ToolTests.SetText(bake, 2, "Esempio_05");
        Button(doc, bake, 1, "BAKE", 1570, 1220);
        var save = Tool(doc, new SaveIfcComponent(), read.Params.Output[0], 650, 1430);
        var dir = Panel(doc, "CARTELLA ARCHIVIO IFC COMPLETO", Path.Combine(Root, "exports"), 30, 1250, 560, 65);
        save.Params.Input[1].AddSource(dir); Button(doc, save, 2, "SAVE ARCHIVE", 330, 1490);
        Out(doc, "ARCHIVIO SALVATO | coordinate originali", save.Params.Output[0], 870, 1360, 650, 130);
        return doc;
    }

    private static GH_Document Explore()
    {
        var doc = new GH_Document();
        Header(doc, "06 | ESPLORA MODELLO, CHIAVI E IDENTIFICATIVI", "Read IFC -> Model Info per schema, unita e conteggi; Property Keys per trovare le chiavi esatte; Select IDs per isolare un oggetto.\nModello incorporato: IFC4, 11 record e 6 mesh. Il pannello ID contiene un GlobalId valido e ID_NON_PRESENTE per mostrare gli ID non trovati.\nCambia la ricerca delle chiavi, copia una chiave nel pannello PARAMETRO e consulta valore, presenza e JSON dell'oggetto selezionato.");
        var read = Reader(doc, "tutorial_A");
        var info = Tool(doc, new ModelInfoComponent(), read.Params.Output[0], 660, 345);
        Out(doc, "SCHEMA", info.Params.Output[1], 870, 170, 240, 75);
        Out(doc, "PRODOTTI / RECORD", info.Params.Output[2], 1140, 170, 250, 75);
        Out(doc, "MESH", info.Params.Output[3], 1420, 170, 240, 75);
        Out(doc, "SCALA LUNGHEZZE IFC -> METRI", info.Params.Output[4], 1690, 170, 350, 75);
        Out(doc, "CLASSI", info.Params.Output[5], 890, 300, 480, 195);
        Out(doc, "CONTEGGI PER CLASSE", info.Params.Output[6], 1410, 300, 430, 195);
        var keys = Tool(doc, new PropertyKeysComponent(), read.Params.Output[1], 660, 685);
        var search = Panel(doc, "CERCA NEL NOME DELLA CHIAVE", "FireRating", 30, 535, 485, 65);
        keys.Params.Input[1].AddSource(search);
        Out(doc, "CHIAVI ESATTE | copia una chiave sotto", keys.Params.Output[0], 890, 585, 680, 155);
        Out(doc, "OCCORRENZE | stessa riga della chiave", keys.Params.Output[1], 1620, 585, 420, 155);
        var model = IfcModel.Load(Path.Combine(Root, "examples", "tutorial_A.ifcdata.zip"));
        var wall = model.Elements.First(e => e.Name.StartsWith("Parete 1"));
        var ids = Tool(doc, new SelectIdsComponent(), read.Params.Output[1], 660, 1020);
        var request = Panel(doc, "ID | GlobalId, #STEP-ID oppure IFC.Key", wall.GlobalId + "\nID_NON_PRESENTE", 30, 855, 520, 105);
        ids.Params.Input[1].AddSource(request);
        Out(doc, "ID NON TROVATI | esempio intenzionale", ids.Params.Output[2], 890, 875, 630, 80);
        var inspect = Tool(doc, new InspectIfcComponent(), ids.Params.Output[0], 1090, 1120);
        Preview(doc, inspect.Params.Output[0], inspect.Params.Output[7], 1470, 1120, "SOLO OGGETTO SELEZIONATO");
        var value = Tool(doc, new PropertyValueComponent(), ids.Params.Output[0], 660, 1450);
        var key = Panel(doc, "PARAMETRO | incolla una chiave da Property Keys", Fire, 30, 1270, 530, 70);
        value.Params.Input[1].AddSource(key);
        Out(doc, "VALORE EFFETTIVO", value.Params.Output[0], 890, 1355, 450, 95);
        Out(doc, "PRESENTE | distingue assente da valore vuoto", value.Params.Output[1], 1380, 1355, 560, 95);
        Out(doc, "JSON COMPLETO | doppio clic per consultare", inspect.Params.Output[6], 890, 1500, 1050, 125);
        return doc;
    }
    private static GH_Document NumericFilters()
    {
        var doc = new GH_Document();
        Header(doc, "07 | FILTRI NUMERICI, BOOLEANI E COMPONENTI NATIVI", "Pareti -> Sort numerico per Tutorial.Indice -> filtro >= soglia -> filtro Selezionabile=true -> preview, somma dei volumi e CSV.\nGli indici sono 2, 10, 1: Sort numerico produce 1, 2, 10. Con soglia 2 sono selezionate due pareti e il volume totale e 9 m3.\nSposta lo slider a 10: resta una parete, 4.5 m3. Il flag false della parete con indice 1 e un valore presente, non un parametro mancante.");
        var read = Reader(doc, "tutorial_numeri");
        var walls = Tool(doc, new InspectIfcComponent(), read.Params.Output[1], 650, 345, "SOLO IfcWall");
        ToolTests.SetText(walls, 1, "IfcWall");
        var sort = Tool(doc, new SortElementsComponent(), walls.Params.Output[1], 1030, 345, "NUMERIC = TRUE");
        ToolTests.SetText(sort, 1, IndexKey); ToolTests.SetBool(sort, 2, true);
        var values = Tool(doc, new PropertyValueComponent(), sort.Params.Output[0], 1440, 345, "INDICI ORDINATI");
        ToolTests.SetText(values, 1, IndexKey);
        Out(doc, "NUMERI | 1, 2, 10", values.Params.Output[2], 1650, 260, 400, 140);
        var key = Panel(doc, "CHIAVE NUMERICA", IndexKey, 30, 525, 510, 65);
        var slider = Place(doc, new GH_NumberSlider(), 110, 695);
        slider.NickName = "SOGLIA INDICE"; slider.Slider.Type = Grasshopper.GUI.Base.GH_SliderAccuracy.Integer;
        slider.Slider.Minimum = 0; slider.Slider.Maximum = 12; slider.SetSliderValue(2);
        var threshold = Filter(doc, sort.Params.Output[0], IndexKey, "2", 650, 715);
        threshold.NickName = "SOGLIA >="; ToolTests.SetText(threshold, 2, ">=");
        threshold.Params.Input[1].AddSource(key); threshold.Params.Input[3].AddSource(slider);
        var enabled = Filter(doc, threshold.Params.Output[0], EnabledKey, "true", 1040, 715);
        enabled.NickName = "SELEZIONABILE";
        var flag = Panel(doc, "CHIAVE BOOLEANA | confronto testuale true", EnabledKey, 760, 500, 630, 65);
        enabled.Params.Input[1].AddSource(flag);
        FixedPreview(doc, enabled.Params.Output[0], Color.SteelBlue, 1500, 715, "PARETI FILTRATE");
        var metrics = Tool(doc, new GeometryMetricsComponent(), enabled.Params.Output[0], 650, 1080);
        var sum = Native(doc, "Mass Addition", metrics.Params.Output[1], 1030, 1080);
        Out(doc, "TOTALE VOLUMI | m3", sum.Params.Output[0], 1260, 995, 570, 130);
        var csv = Csv(doc, enabled.Params.Output[0], "07_filtro_numerico", 850, 1440);
        var columns = (GH_Panel)csv.Params.Input[1].Sources.Single();
        columns.UserText = string.Join("\n", new[] { "IFC.GlobalId", "IFC.Name", IndexKey, EnabledKey });
        return doc;
    }
    private static GH_Document BakeAndRead()
    {
        var doc = new GH_Document();
        Header(doc, "08 | BAKE E RILETTURA DEGLI ATTRIBUTI RHINO", "Read IFC -> oggetti con mesh -> Bake IFC -> Read Baked: premi BAKE e consulta GUID Rhino, chiavi, valori e JSON.\nAttesi: 6 mesh, layer Esempio_08::sorgente::IfcClass, tutti i parametri in User Text. UPDATE e True: un secondo bake aggiorna gli stessi oggetti.\nSe modifichi gli User Text in Rhino premi REFRESH. JSON conserva il record IFC originale del bake. Salva il documento .3dm per conservare gli oggetti.");
        var read = Reader(doc, "tutorial_A");
        var filter = Filter(doc, read.Params.Output[1], "IFC.GeometryStatus", "ok", 650, 345);
        var inspect = Tool(doc, new InspectIfcComponent(), filter.Params.Output[0], 1030, 345);
        Preview(doc, inspect.Params.Output[0], inspect.Params.Output[7], 1430, 345, "ANTEPRIMA PRIMA DEL BAKE");
        Note(doc, "DOPO IL BAKE", "Disattiva Custom Preview per vedere solo gli oggetti Rhino. Gli attributi si trovano in Proprieta > Testo utente attributo.", 1640, 245, 450, 130);
        var bake = Tool(doc, new BakeIfcComponent(), filter.Params.Output[0], 650, 740);
        ToolTests.SetText(bake, 2, "Esempio_08"); Button(doc, bake, 1, "BAKE", 410, 790);
        var baked = Tool(doc, new ReadBakedComponent(), bake.Params.Output[0], 1080, 740);
        ToolTests.SetText(baked, 1, "Esempio_08"); Button(doc, baked, 2, "REFRESH", 900, 870);
        var length = Native(doc, "List Length", baked.Params.Output[0], 1490, 740);
        Out(doc, "OGGETTI RHINO | 0 prima, 6 dopo BAKE", length.Params.Output[0], 1670, 660, 450, 105);
        Out(doc, "REPORT BAKE", bake.Params.Output[1], 30, 505, 540, 130);
        Out(doc, "GUID RHINO | diversi dai GlobalId IFC", baked.Params.Output[0], 30, 1040, 610, 180);
        Out(doc, "CHIAVI USER TEXT | un ramo per oggetto", baked.Params.Output[2], 690, 1040, 680, 245);
        Out(doc, "VALORI | righe allineate alle chiavi", baked.Params.Output[3], 1410, 1040, 700, 245);
        Out(doc, "METADATI IFC ORIGINALI | JSON", baked.Params.Output[4], 690, 1340, 1420, 165);
        return doc;
    }

    private static T Find<T>(GH_Document doc) where T : GH_Component { return doc.Objects.OfType<T>().Single(); }
    private static GH_Component FindNative(GH_Document doc, string name) { return doc.Objects.OfType<GH_Component>().Single(c => c.Name == name); }
    private static double[] Numbers(GH_Component c, int output) { return c.Params.Output[output].VolatileData.AllData(true).Cast<GH_Number>().Select(x => x.Value).ToArray(); }
    private static string[] Text(GH_Component c, int output) { return c.Params.Output[output].VolatileData.AllData(true).Cast<GH_String>().Select(x => x.Value).ToArray(); }
    private static IfcElement[] Elements(GH_Component c, int output)
    { return c.Params.Output[output].VolatileData.AllData(true).Cast<GH_ObjectWrapper>().Select(x => (IfcElement)x.Value).ToArray(); }
    private static void NoErrors(GH_Document doc, string name)
    {
        var errors = doc.Objects.OfType<GH_Component>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => c.Name + ": " + e)).ToArray();
        Check(errors.Length == 0, name + " | nessun errore GH: " + string.Join("; ", errors));
    }
    private static void Verify(GH_Document doc, int number)
    {
        if (number == 1)
        {
            Check(Elements(Find<SortElementsComponent>(doc), 0).Length == 3, "01 | abaco di 3 pareti");
            Check(Text(Find<PropertyValueComponent>(doc), 0).OrderBy(x => x).SequenceEqual(new[] { "REI 120", "REI 120", "REI 60" }), "01 | proprieta di istanza e tipo");
            var volumes = Find<GeometryMetricsComponent>(doc).Params.Output[1].VolatileData.AllData(true).Cast<GH_Number>().Select(x => x.Value);
            Check(Math.Abs(volumes.Sum() - 12.0) < 1e-5, "01 | volume totale 12 m3");
        }
        if (number == 2)
        {
            var group = Find<GroupByComponent>(doc); var labels = Text(group, 1);
            var counts = group.Params.Output[2].VolatileData.AllData(true).Cast<GH_Integer>().Select(x => x.Value).ToArray();
            Check(labels.Length == 2 && labels.Select((label, i) => counts[i] == (int)Expected["floor_counts"][label]).All(x => x), "02 | gruppi per piano: 4 + 2");
            Check(Find<ColorByComponent>(doc).Params.Output[0].VolatileDataCount == 6, "02 | 6 mesh colorate");
        }
        if (number == 3)
        {
            var c = Find<ValidateElementsComponent>(doc);
            Check(Elements(c, 0).Length == 2 && Elements(c, 1).Single().GlobalId == (string)Expected["missing_code_global_id"], "03 | due conformi e parete corretta segnalata");
            Check(Text(c, 3).Any(t => t.Contains(Code)), "03 | chiave mancante esplicita nel report");
        }
        if (number == 4)
        {
            var diff = Find<CompareRevisionsComponent>(doc);
            foreach (var pair in new[] { Tuple.Create(0, "added"), Tuple.Create(1, "removed"), Tuple.Create(3, "changed") })
                Check(Elements(diff, pair.Item1).Select(x => x.GlobalId).OrderBy(x => x).SequenceEqual(Expected["revision"][pair.Item2].Values<string>().OrderBy(x => x)), "04 | GlobalId " + pair.Item2);
            Check(Elements(diff, 4).Length == 8, "04 | otto record invariati");
            Check(Text(diff, 5).Any(t => t.Contains("FireRating")) && Text(diff, 5).Any(t => t.Contains("geometry.mesh_or_placement")), "04 | riconosciute modifica parametro e traslazione");
        }
        if (number == 5)
        {
            var offset = ((GH_Vector)Find<OriginComponent>(doc).Params.Output[0].VolatileData.AllData(true).Single()).Value;
            Check(Math.Abs(offset.X + 650002.5) < 1e-5 && Math.Abs(offset.Y + 4860002.15) < 1e-5 && offset.Z == 0, "05 | offset comune in metri con quota Z preservata");
            var meshes = Find<InspectIfcComponent>(doc).Params.Output[0].VolatileData.AllData(true).Cast<GH_Mesh>().Select(x => x.Value).ToArray();
            var bounds = BoundingBox.Empty; foreach (var mesh in meshes) bounds.Union(mesh.GetBoundingBox(true));
            Check(meshes.Length == 6 && Math.Abs(bounds.Center.X) < 0.01 && Math.Abs(bounds.Center.Y) < 0.01 && Math.Abs(bounds.Max.Z - 6200) < 0.01, "05 | preview centrata e quota 6200 mm");
        }
        if (number == 6)
        {
            var info = Find<ModelInfoComponent>(doc);
            Check(Text(info, 1).Single() == "IFC4" && ((GH_Integer)info.Params.Output[2].VolatileData.AllData(true).Single()).Value == 11 && ((GH_Integer)info.Params.Output[3].VolatileData.AllData(true).Single()).Value == 6, "06 | info: IFC4, 11 record, 6 mesh");
            Check(Math.Abs(Numbers(info, 4).Single() - 0.001) < 1e-12, "06 | scala mm IFC -> metri");
            var keys = Find<PropertyKeysComponent>(doc); var found = Text(keys, 0);
            int fireIndex = Array.IndexOf(found, Fire);
            Check(fireIndex >= 0 && keys.Params.Output[1].VolatileData.AllData(true).Cast<GH_Integer>().ElementAt(fireIndex).Value == 3, "06 | scoperta FireRating con tre occorrenze");
            var ids = Find<SelectIdsComponent>(doc);
            Check(Elements(ids, 0).Single().Name.StartsWith("Parete 1") && Text(ids, 2).SequenceEqual(new[] { "ID_NON_PRESENTE" }) && Text(ids, 3).Length == 0, "06 | selezione ID e report identificativo assente");
            Check(Text(Find<PropertyValueComponent>(doc), 0).Single() == "REI 60" && Text(Find<InspectIfcComponent>(doc), 6).Single().Contains("property_graph"), "06 | attributo e JSON della parete selezionata");
        }
        if (number == 7)
        {
            Check(Numbers(Find<PropertyValueComponent>(doc), 2).SequenceEqual(new[] { 1.0, 2.0, 10.0 }), "07 | ordine numerico 1, 2, 10");
            var selected = doc.Objects.OfType<FilterPropertyComponent>().Single(c => c.NickName == "SELEZIONABILE");
            Check(Elements(selected, 0).Length == 2 && Math.Abs(Numbers(FindNative(doc, "Mass Addition"), 0).Single() - 9) < 1e-5, "07 | filtri AND: due pareti, somma nativa 9 m3");
        }
        if (number == 8)
        {
            Check(Elements(Find<FilterPropertyComponent>(doc), 0).Length == 6, "08 | sei elementi pronti per il bake");
            Check(Find<ReadBakedComponent>(doc).Params.Output[0].VolatileDataCount == 0, "08 | rilettura vuota prima del bake");
        }
    }
    private static void Disconnect(GH_Component c, int index) { c.Params.Input[index].RemoveAllSources(); }
    private static void Exercise(GH_Document doc, RhinoDoc rhino, int number)
    {
        if (number == 6)
        {
            var ids = Find<SelectIdsComponent>(doc); var wall = Elements(ids, 0).Single();
            Disconnect(ids, 1); ToolTests.SetText(ids, 1, "#" + wall.StepId); doc.NewSolution(true);
            Check(Elements(ids, 0).Single().GlobalId == wall.GlobalId && Text(ids, 2).Length == 0, "06 | selezione alternativa tramite STEP-ID");
            ToolTests.SetText(ids, 1, wall.Key); doc.NewSolution(true);
            Check(Elements(ids, 0).Single().GlobalId == wall.GlobalId, "06 | selezione alternativa tramite IFC.Key");
            NoErrors(doc, "Interazione 06");
        }
        if (number == 7)
        {
            var slider = doc.Objects.OfType<GH_NumberSlider>().Single();
            var selected = doc.Objects.OfType<FilterPropertyComponent>().Single(c => c.NickName == "SELEZIONABILE");
            slider.SetSliderValue(10); slider.ExpireSolution(false); doc.NewSolution(true);
            Check(Elements(selected, 0).Length == 1 && Math.Abs(Numbers(FindNative(doc, "Mass Addition"), 0).Single() - 4.5) < 1e-5, "07 | slider 10 aggiorna selezione e somma a 4.5 m3");
            slider.SetSliderValue(0); slider.ExpireSolution(false); doc.NewSolution(true);
            Check(Elements(selected, 0).Length == 2 && Elements(selected, 1).Single().Name == "Parete P1", "07 | il secondo filtro esclude esplicitamente il valore false");
            slider.SetSliderValue(2); slider.ExpireSolution(false); doc.NewSolution(true);
            NoErrors(doc, "Interazione 07");
        }
        if (number == 1 || number == 3 || number == 4 || number == 7)
        {
            var csv = Find<ExportCsvComponent>(doc); string path = Path.Combine(OutputRoot, "esempio_0" + number + ".csv");
            Disconnect(csv, 2); Disconnect(csv, 3); ToolTests.SetText(csv, 2, path); ToolTests.SetBool(csv, 3, true);
            doc.NewSolution(true); NoErrors(doc, "WRITE CSV " + number);
            Check(File.Exists(path) && Text(csv, 0).Single() == path, "0" + number + " | pulsante CSV collegato alla selezione");
        }
        if (number == 2)
        {
            var bake = Find<BakeIfcComponent>(doc); Disconnect(bake, 1); ToolTests.SetBool(bake, 1, true); doc.NewSolution(true);
            NoErrors(doc, "BAKE 02");
            Check(rhino.Objects.Count == 6 && Find<ReadBakedComponent>(doc).Params.Output[0].VolatileDataCount == 6, "02 | bake di entrambi i rami e rilettura dei 6 GUID");
            Check(rhino.Objects.GetObjectList(Rhino.DocObjects.ObjectType.Mesh).All(o => o.Attributes.GetUserString("IFC.MetadataJSON") != null && rhino.Layers[o.Attributes.LayerIndex].FullPath.EndsWith("::" + o.Attributes.GetUserString("IFC.Class"))), "02 | layer per classe e metadati conservati");
        }
        if (number == 5)
        {
            var export = Find<Export3dmComponent>(doc); string path = Path.Combine(OutputRoot, "esempio_05.3dm");
            Disconnect(export, 1); Disconnect(export, 2); ToolTests.SetText(export, 1, path); ToolTests.SetBool(export, 2, true);
            doc.NewSolution(true); NoErrors(doc, "EXPORT 05");
            Check(rhino.Objects.Count == 0, "05 | export non aggiunge oggetti al documento attivo");
            using (var file = Rhino.FileIO.File3dm.Read(path))
            {
                var bounds = BoundingBox.Empty;
                foreach (var obj in file.Objects.Where(o => o != null)) bounds.Union(obj.Geometry.GetBoundingBox(true));
                Check(file.Objects.Count == 6 && Math.Abs(bounds.Center.X) < 0.01 && Math.Abs(bounds.Center.Y) < 0.01 && Math.Abs(bounds.Max.Z - 6200) < 0.01, "05 | 3DM e preview condividono l'offset");
                Check(file.Objects.Where(o => o != null).All(o => o.Attributes.GetUserString("IFC.MetadataJSON") != null), "05 | attributi IFC nel 3DM");
            }
            var save = Find<SaveIfcComponent>(doc); Disconnect(save, 1); Disconnect(save, 2);
            ToolTests.SetText(save, 1, OutputRoot); ToolTests.SetBool(save, 2, true); doc.NewSolution(true); NoErrors(doc, "ARCHIVE 05");
            string archive = Text(save, 0).Single();
            Check(IfcModel.Load(archive).Elements.Count == 11, "05 | archivio conserva tutti gli 11 record");
        }
        if (number == 8)
        {
            var bake = Find<BakeIfcComponent>(doc); var baked = Find<ReadBakedComponent>(doc);
            Disconnect(bake, 1); ToolTests.SetBool(bake, 1, true); doc.NewSolution(true);
            var ids = Text(bake, 0);
            Check(ids.Length == 6 && rhino.Objects.Count == 6 && Text(baked, 0).Length == 6, "08 | bake, GUID e rilettura dei sei oggetti");
            Check(Text(baked, 2).Contains(Fire) && Text(baked, 3).Contains("REI 60") && Text(baked, 4).Length == 6, "08 | User Text e JSON visibili nei pannelli");
            Check(((GH_Integer)FindNative(doc, "List Length").Params.Output[0].VolatileData.AllData(true).Single()).Value == 6, "08 | conteggio nativo dei GUID");
            ToolTests.SetBool(bake, 1, false); doc.NewSolution(true); ToolTests.SetBool(bake, 1, true); doc.NewSolution(true);
            Check(rhino.Objects.Count == 6 && Text(bake, 0).SequenceEqual(ids), "08 | secondo bake aggiorna senza duplicati");
            var obj = rhino.Objects.FindId(new Guid(ids[0])); var attributes = obj.Attributes.Duplicate();
            attributes.SetUserString("Verifica.Utente", "Controllato");
            Check(rhino.Objects.ModifyAttributes(obj.Id, attributes, true), "08 | modifica User Text nel documento di prova");
            Disconnect(baked, 2); ToolTests.SetBool(baked, 2, true); doc.NewSolution(true);
            var keys = Text(baked, 2); var values = Text(baked, 3); int at = Array.IndexOf(keys, "Verifica.Utente");
            Check(at >= 0 && values[at] == "Controllato", "08 | Refresh rilegge i dati modificati in Rhino");
            NoErrors(doc, "Interazione 08");
        }
    }
    private static void Snapshot(GH_Document doc, string name, int height)
    {
        using (var canvas = new GH_Canvas())
        {
            canvas.Size = new Size(1510, height); canvas.Document = doc; canvas.CreateControl();
            canvas.Viewport.Size = canvas.Size; canvas.Viewport.Zoom = 0.67f; canvas.Viewport.Target = new Point(0, 0); canvas.Viewport.ComputeProjection();
            using (var bitmap = new Bitmap(canvas.Width, canvas.Height))
            { canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(OutputRoot, name + ".png")); }
            canvas.Document = null;
        }
    }
    internal static void Generate(string root, Action<bool, string> check)
    {
        Root = root; Check = check; Expected = JObject.Parse(File.ReadAllText(Path.Combine(root, "examples", "scenari.json")));
        OutputRoot = Path.Combine(root, "test-output", "examples-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(OutputRoot);
        var names = new[] { "01_Abaco_pareti", "02_Piani_e_colori", "03_Controllo_parametri", "04_Confronto_revisioni", "05_Origine_locale_export", "06_Esplora_modello", "07_Filtri_numerici", "08_Bake_e_rilettura" };
        Func<GH_Document>[] factories = { Schedule, Floors, Quality, Revision, LocalOrigin, Explore, NumericFilters, BakeAndRead };
        var coverage = new Dictionary<string, HashSet<string>>();
        GH_Document.EnableSolutions = true;
        for (int i = 0; i < factories.Length; i++)
        using (var rhino = RhinoDoc.CreateHeadless(null))
        {
            RhinoDoc.ActiveDoc = rhino; rhino.ModelUnitSystem = UnitSystem.Millimeters;
            string file = Path.Combine(root, "examples", names[i] + ".gh");
            using (var doc = factories[i]())
            {
                doc.Enabled = true; doc.NewSolution(true); NoErrors(doc, names[i]); Verify(doc, i + 1);
                Check(rhino.Objects.Count == 0 && doc.Objects.OfType<ExportCsvComponent>().All(c => Text(c, 0).Single() == ""), names[i] + " | nessun bake/export all'apertura");
                foreach (string extension in new[] { ".gh", ".ghx" })
                { var archive = new GH_Archive(); Check(archive.AppendObject(doc, "Definition") && archive.WriteToFile(Path.ChangeExtension(file, extension), true, false), names[i] + " | salvato " + extension); }
                Snapshot(doc, names[i], i >= 4 ? 1130 : 960);
            }
            var io = new GH_DocumentIO(); Check(io.Open(file), names[i] + " | riaperto da disco");
            using (var reopened = io.Document)
            {
                reopened.Enabled = true; reopened.NewSolution(true); NoErrors(reopened, "Riapertura " + names[i]); Verify(reopened, i + 1);
                Check(reopened.Objects.OfType<ReadIfcComponent>().All(c => c.Params.Output[1].VolatileDataCount == 11), names[i] + " | modelli incorporati recuperati");
                Check(rhino.Objects.Count == 0, names[i] + " | riapertura senza bake");
                foreach (var component in reopened.Objects.OfType<IfcComponent>())
                {
                    HashSet<string> files;
                    if (!coverage.TryGetValue(component.GetType().FullName, out files))
                    { files = new HashSet<string>(); coverage.Add(component.GetType().FullName, files); }
                    files.Add(names[i] + ".gh");
                }
                Exercise(reopened, rhino, i + 1);
            }
        }
        var expectedTypes = typeof(IfcComponent).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IfcComponent).IsAssignableFrom(t)).ToArray();
        Check(expectedTypes.Length == 19 && new HashSet<string>(expectedTypes.Select(t => t.FullName)).SetEquals(coverage.Keys), "Copertura: tutti i 19 componenti nei file GH riaperti");
        var manifest = new JArray();
        foreach (var type in expectedTypes.OrderBy(t => t.Name))
        {
            var component = (GH_Component)Activator.CreateInstance(type);
            manifest.Add(new JObject(new JProperty("component", component.Name), new JProperty("guid", component.ComponentGuid.ToString()), new JProperty("examples", new JArray(coverage[type.FullName].OrderBy(x => x)))));
        }
        File.WriteAllText(Path.Combine(root, "examples", "componenti-esempi.json"), manifest.ToString());
        File.WriteAllText(Path.Combine(root, "test-output", "last-examples-output.txt"), OutputRoot);
        Console.WriteLine("EXAMPLES_OUTPUT=" + OutputRoot);
    }
}
