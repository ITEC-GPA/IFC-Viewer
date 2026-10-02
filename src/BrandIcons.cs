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
            return GH_LoadingInstruction.Proceed;
        }
    }
}
