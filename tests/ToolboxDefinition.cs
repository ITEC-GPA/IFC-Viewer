using System;
using System.Drawing;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using IfcViewer;

internal static class ToolboxDefinition
{
    private static T Place<T>(GH_Document doc, T item, int x, int y) where T : IGH_DocumentObject
    { item.CreateAttributes(); item.Attributes.Pivot = new PointF(x, y); doc.AddObject(item, false); return item; }
    private static GH_Panel Panel(GH_Document doc, string name, string text, int x, int y, int w = 360, int h = 100)
    {
        var panel = Place(doc, new GH_Panel(), x, y); panel.NickName = name; panel.UserText = text;
        panel.Attributes.Bounds = new RectangleF(x, y, w, h); return panel;
    }
    private static T Tool<T>(GH_Document doc, T component, IGH_Param source, int x, int y) where T : GH_Component
    {
        Place(doc, component, x, y); component.Params.Input[0].AddSource(source);
        Panel(doc, component.Name, component.Description, x - 145, y - 140, 290, 85);
        component.Hidden = true; return component;
    }
    private static void Output(GH_Document doc, string title, IGH_Param source, int x, int y, int w = 310, int h = 125)
    { var panel = Panel(doc, title, "", x, y, w, h); panel.AddSource(source); }
    internal static void Add(GH_Document doc, ReadIfcComponent read, InspectIfcComponent inspect, BakeIfcComponent bake, IfcModel model, string root)
    {
        var all = inspect.Params.Output[1];
        Panel(doc, "02 | INTERROGA E ORGANIZZA", "Tutti questi componenti lavorano sulla selezione di IFC Elements.\nLe chiavi sono quelle di IFC Property Keys.\nFiltro numerico: punto decimale, unita originali IFC.\nIl risultato di un filtro puo essere collegato a Bake, Export o a un altro filtro.", 1740, 20, 1220, 120);
        var info = Tool(doc, new ModelInfoComponent(), read.Params.Output[0], 1950, 335);
        Output(doc, "CLASSI PRESENTI", info.Params.Output[5], 2200, 210);
        Output(doc, "QUANTITA PER CLASSE", info.Params.Output[6], 2550, 210);
        var keys = Tool(doc, new PropertyKeysComponent(), all, 1950, 650); ToolTests.SetText(keys, 1, "FireRating");
        Output(doc, "CHIAVI DISPONIBILI | ricerca FireRating", keys.Params.Output[0], 2210, 530, 650, 160);
        var key = Panel(doc, "PARAMETRO DA LEGGERE / FILTRARE", "IFC.Property.Pset_WallCommon.FireRating", 1740, 790, 420, 75);
        var value = Tool(doc, new PropertyValueComponent(), all, 2300, 1000); value.Params.Input[1].AddSource(key);
        Output(doc, "VALORI | vuoto = assente", value.Params.Output[0], 2570, 940);
        var filter = Tool(doc, new FilterPropertyComponent(), all, 1950, 1260); filter.Params.Input[1].AddSource(key); ToolTests.SetText(filter, 3, "REI 60");
        Output(doc, "FILTRO ESEMPIO | FireRating = REI 60", filter.Params.Output[0], 2220, 1200, 600);
        var group = Tool(doc, new GroupByComponent(), all, 1950, 1560); ToolTests.SetText(group, 1, "IFC.Container.name");
        Output(doc, "GRUPPI PER PIANO / CONTENITORE", group.Params.Output[1], 2220, 1500);
        Output(doc, "CONTEGGI", group.Params.Output[2], 2550, 1500);
        var sort = Tool(doc, new SortElementsComponent(), all, 1950, 1860);
        Output(doc, "OGGETTI ORDINATI PER NOME", sort.Params.Output[0], 2220, 1800, 640);
        var ids = Tool(doc, new SelectIdsComponent(), all, 1950, 2170);
        var id = Panel(doc, "GLOBALID DI ESEMPIO | sostituire", model.Elements.First(e => e.IfcClass == "IfcWall").GlobalId, 1740, 2260, 420, 65);
        ids.Params.Input[1].AddSource(id); Output(doc, "OGGETTI SELEZIONATI PER ID", ids.Params.Output[0], 2230, 2110, 630);

        Panel(doc, "03 | VISUALIZZAZIONE E MISURE", "Le aree e i volumi derivano dalle mesh e sono sempre in m2/m3.\nI box/centri per la viewport sono nelle unita Rhino.\nLocal Origin produce un offset comune: collegarlo sia alla preview sia al bake/export.\nColor By fornisce G e C da collegare a Custom Preview; qui la preview aggiuntiva e disattivata.", 3160, 20, 1220, 120);
        var metrics = Tool(doc, new GeometryMetricsComponent(), all, 3370, 350);
        Output(doc, "AREA SUPERFICIALE | m2", metrics.Params.Output[0], 3650, 245);
        Output(doc, "VOLUME | m3; NaN se non calcolabile", metrics.Params.Output[1], 4000, 245);
        var origin = Tool(doc, new OriginComponent(), all, 3370, 705);
        Output(doc, "OFFSET SUGGERITO | metri; non ancora applicato", origin.Params.Output[0], 3650, 640, 650);
        var color = Tool(doc, new ColorByComponent(), all, 3370, 1080); ToolTests.SetText(color, 1, "IFC.Container.name");
        Output(doc, "LEGENDA COLORI PER PIANO", color.Params.Output[2], 3650, 980);
        Output(doc, "CONTEGGI PER COLORE", color.Params.Output[4], 4000, 980);
        var proxy = Grasshopper.Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Desc.Name == "Custom Preview");
        if (proxy != null)
        {
            var preview = Place(doc, (GH_Component)proxy.CreateInstance(), 3780, 1230);
            preview.Params.Input[0].AddSource(color.Params.Output[0]); preview.Params.Input[1].AddSource(color.Params.Output[1]); preview.Hidden = true;
        }

        Panel(doc, "04 | CONTROLLO DATI E REVISIONI", "Check Data verifica le chiavi richieste e, opzionalmente, la presenza di mesh.\nCompare Revisions confronta un modello prima/dopo tramite GlobalId.\nEsempio iniziale: entrambi gli input sono sullo stesso modello, quindi nessuna modifica.\nAggiungere un secondo Read IFC e collegare Models all'input B per confrontare un'altra revisione.", 3160, 1400, 1220, 120);
        var check = Tool(doc, new ValidateElementsComponent(), all, 3370, 1730); ToolTests.SetText(check, 1, "IFC.GlobalId");
        Output(doc, "PROBLEMI | ramo = indice oggetto originale", check.Params.Output[3], 3650, 1640, 650, 140);
        var diff = Tool(doc, new CompareRevisionsComponent(), read.Params.Output[0], 3370, 2080); diff.Params.Input[1].AddSource(read.Params.Output[0]);
        Output(doc, "CONFRONTO REVISIONI", diff.Params.Output[6], 3650, 1980, 650);
        Output(doc, "CAMPI MODIFICATI", diff.Params.Output[5], 3650, 2190, 650);

        Panel(doc, "05 | ESPORTA E RILEGGI IL BAKE", "CSV: una riga per oggetto, parametri per colonna. 3DM: mesh, layer e User Text.\nWRITE agisce solo quando premi il Button; Overwrite e disattivato.\nBake ed export uniscono automaticamente i rami di elementi in una lista.\nRead Baked consulta i dati gia presenti nel documento Rhino; usa Refresh dopo modifiche.", 30, 1270, 1500, 115);
        var csv = Tool(doc, new ExportCsvComponent(), all, 420, 1710);
        var csvFile = Panel(doc, "FILE CSV", Path.Combine(root, "exports", "abaco.csv"), 30, 1430, 480, 65); csv.Params.Input[2].AddSource(csvFile);
        ToolTests.SetText(csv, 1, "IFC.GlobalId", "IFC.Class", "IFC.Name", "IFC.Container.name", "IFC.Property.Pset_WallCommon.FireRating");
        var csvWrite = Place(doc, new GH_ButtonObject(), 160, 1810); csvWrite.NickName = "WRITE CSV"; csv.Params.Input[3].AddSource(csvWrite);
        Output(doc, "CSV SALVATO", csv.Params.Output[0], 640, 1610, 480);
        var file3dm = Tool(doc, new Export3dmComponent(), all, 420, 2150);
        var path3dm = Panel(doc, "FILE 3DM", Path.Combine(root, "exports", "selezione.3dm"), 30, 1880, 480, 65); file3dm.Params.Input[1].AddSource(path3dm);
        var write3dm = Place(doc, new GH_ButtonObject(), 160, 2260); write3dm.NickName = "WRITE 3DM"; file3dm.Params.Input[2].AddSource(write3dm);
        Output(doc, "REPORT 3DM", file3dm.Params.Output[1], 640, 2060, 700);
        var baked = Tool(doc, new ReadBakedComponent(), bake.Params.Output[0], 420, 2530);
        var refresh = Place(doc, new GH_ButtonObject(), 160, 2630); refresh.NickName = "REFRESH"; baked.Params.Input[2].AddSource(refresh);
        Output(doc, "CHIAVI DAGLI OGGETTI RHINO", baked.Params.Output[2], 640, 2450, 450, 180);
        Output(doc, "VALORI DAGLI OGGETTI RHINO", baked.Params.Output[3], 1140, 2450, 450, 180);
    }
}
