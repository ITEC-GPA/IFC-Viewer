using System;
using System.Collections.Concurrent;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace IfcViewer
{
    internal static class BrandIcons
    {
        private static readonly ConcurrentDictionary<string, Bitmap> Cache = new ConcurrentDictionary<string, Bitmap>();
        internal static Bitmap Get(string name)
        {
            return Cache.GetOrAdd(name, key =>
            {
                using (var stream = typeof(BrandIcons).Assembly.GetManifestResourceStream("Meerkat.Icons." + key + ".png"))
                {
                    if (stream == null) throw new InvalidOperationException("Icona Meerkat mancante: " + key);
                    using (var bitmap = new Bitmap(stream)) return new Bitmap(bitmap);
                }
            });
        }
    }

    public sealed class MeerkatPriority : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            Instances.ComponentServer.AddCategoryIcon("Meerkat", BrandIcons.Get("Meerkat"));
            Instances.ComponentServer.AddCategorySymbolName("Meerkat", 'M');
            // ComponentServer may register a COFF-loaded assembly only after PriorityLoad.
            // Retry at the first idle event when its physical path is available.
            EventHandler initialize = null;
            initialize = (sender, args) =>
            {
                Rhino.RhinoApp.Idle -= initialize;
                try { Gpc.PluginChecks.PluginBuildCheck.Start(typeof(MeerkatPriority).Assembly, "Meerkat", true, Engine.PluginDirectory); }
                catch (Exception ex) { Rhino.RhinoApp.WriteLine("Meerkat: verifica build non disponibile: " + ex.Message); }
            };
            if (!string.IsNullOrEmpty(typeof(MeerkatPriority).Assembly.Location))
                Gpc.PluginChecks.PluginBuildCheck.Start(typeof(MeerkatPriority).Assembly, "Meerkat");
            else Rhino.RhinoApp.Idle += initialize;
            return GH_LoadingInstruction.Proceed;
        }
    }
}
