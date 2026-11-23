using System;
using System.IO;
using System.Reflection;
using System.Xml;
using Verse;

// Same operation the model binder's exported pack ships (its Patches/*.xml reference IsoRimPackGate.PatchOperation_
// IfIsoRimApi). Keeping the namespace and type name means newly exported packs can be dropped in unchanged.
namespace IsoRimPackGate
{
    // Applies the inner operation only when IsoRim reports at least this API version; an older IsoRim leaves the
    // thing untouched instead of half-applying a model binding.
    public class PatchOperation_IfIsoRimApi : PatchOperation
    {
        public int minVersion;
        public PatchOperation apply;

        private static int cached = -1;

        protected override bool ApplyWorker(XmlDocument xml)
        {
            if (apply == null || IsoRimApi() < minVersion)
            {
                return true;
            }
            return apply.Apply(xml);
        }

        public static int IsoRimApi()
        {
            if (cached >= 0)
            {
                return cached;
            }
            int result = 0;
            try
            {
                Type type = GenTypes.GetTypeInAnyAssembly("IsoRim.Api", null);
                PropertyInfo property = type != null ? type.GetProperty("Version", BindingFlags.Static | BindingFlags.Public) : null;
                if (property != null)
                {
                    result = (int)property.GetValue(null);
                }
            }
            catch (Exception)
            {
                result = 0;
            }
            cached = result;
            return result;
        }
    }
}
