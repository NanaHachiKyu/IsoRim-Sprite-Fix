using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using IsoRim;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRimWeaponCards
{
    // IsoRim can stand a building up as a "card" (立牌). The building's own drawing then goes through
    // Thing.DynamicDrawPhaseAt as usual and lands flat on the ground, and anything it draws itself (a body on a stake,
    // heads, blood, the pole tip...) lands there too.
    //
    // Same shape as Drawers, but for buildings: every drawing method found inside a card building is listed with its
    // own on/off, left/up/depth and render queue. While the building draws, the method that is drawing is looked up by
    // walking the stack; if it is switched on, its draw is folded into the building's card. Anything not listed is
    // left exactly as the game draws it.
    //
    // Placement uses the axes that live inside a card plane, applied BEFORE the card transform:
    //   left -> world X, up -> world Z   (no scale compensation; measured right in game)
    // and depth after the transform, along -camera forward.
    internal static class BuildingCards
    {
        private const int MaxSeen = 200;
        private const char Sep = ';';

        [ThreadStatic] private static int depth;
        [ThreadStatic] private static Matrix4x4 upright;
        [ThreadStatic] private static float commonAltitude;
        // Does this building actually hold a drawn pawn? Only those need their contents folded (an impaling stake).
        // Everything else that IsoRim turns into a card (shelves, statues, towers...) can be skipped outright, which
        // is what keeps the per-draw stack walk below from running on every card building on screen.
        [ThreadStatic] private static bool holdsPawn;
        // Hard cap on stack walks per frame, so a screen full of card buildings cannot stall the game.
        [ThreadStatic] private static int scanBudgetFrame = -1;
        [ThreadStatic] private static int scanBudget;
        private const int ScanBudgetPerFrame = 8;

        private static readonly Dictionary<string, DrawerTune> tunes = new Dictionary<string, DrawerTune>();
        private static MethodInfo shapeCardIs;
        private static FieldInfo drawHookActive;
        private static MethodInfo scopePrefix;
        private static MethodInfo scopeFinalizer;

        // True while IsoRim's own draw hook is transforming draws; multiplying on top would double it.
        internal static bool IsoRimOwns
        {
            get
            {
                try
                {
                    return drawHookActive != null && (bool)drawHookActive.GetValue(null);
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static bool Active
        {
            get
            {
                WeaponCardSettings s = WeaponCardMod.Settings;
                return depth > 0 && s != null && s.buildingEnabled;
            }
        }

        #region settings

        internal static IEnumerable<DrawerTune> All
        {
            get { return tunes.Values; }
        }

        internal static DrawerTune Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            DrawerTune t;
            if (tunes.TryGetValue(key, out t))
            {
                return t;
            }
            t = new DrawerTune { key = key, on = true };
            // Placements measured in game for the impaling stake. They are only used when the mod that owns the
            // building is actually loaded, so nothing is applied for a mod that is not there.
            string preset;
            if (Builtins.TryGetValue(key, out preset) && OwnerLoaded(key))
            {
                Parse(preset, t);
            }
            tunes[key] = t;
            return t;
        }

        // on;useQueue;queue;shiftX;shiftY;shiftZ
        private static void Parse(string text, DrawerTune t)
        {
            string[] parts = text.Split(Sep);
            if (parts.Length < 6)
            {
                return;
            }
            bool.TryParse(parts[0], out t.on);
            bool.TryParse(parts[1], out t.useQueue);
            int q;
            if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out q))
            {
                t.queue = q;
            }
            float x;
            if (float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out x))
            {
                t.shiftX = x;
            }
            float y;
            if (float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            {
                t.shiftY = y;
            }
            float z;
            if (float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                t.shiftZ = z;
            }
        }

        // A preset only applies when the mod it belongs to is loaded; a missing mod then simply has nothing to do.
        private static bool OwnerLoaded(string key)
        {
            if (key.StartsWith("VisceralAnatomy."))
            {
                return ModLoaded("tanyavonhart.visceralanatomy");
            }
            return true;
        }

        internal static bool ModLoaded(string packageId)
        {
            try
            {
                foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
                {
                    if (mod != null && mod.PackageId == packageId)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        // Defaults measured in game (left/up/depth in cards), applied to a freshly found drawing method.
        private static readonly Dictionary<string, string> Builtins = new Dictionary<string, string>
        {
            { "VisceralAnatomy.Building_VisceralStake|DrawInFrame", "True;True;2901;0.000;0.490;0.420" },
            { "VisceralAnatomy.VisceralStakeAgony|DrawPoleBlood", "True;True;2901;0.000;0.490;0.420" },
            { "VisceralAnatomy.VisceralStakePose34|DrawNeckBlood", "True;True;2901;0.000;0.280;-0.050" },
            { "VisceralAnatomy.VisceralStakeAgony|DrawTip", "True;True;2901;0.000;0.360;-0.090" },
            { "Verse.GenDraw|DrawMeshNowOrLater", "True;True;2901;0.000;0.360;-0.010" }
        };

        // Puts one method back to its built-in placement (or to neutral when it has none).
        internal static void ResetToDefault(DrawerTune t)
        {
            if (t == null)
            {
                return;
            }
            t.on = true;
            t.useQueue = true;
            t.queue = 2901;
            t.shiftX = 0f;
            t.shiftY = 0f;
            t.shiftZ = 0f;
            string preset;
            if (Builtins.TryGetValue(t.key, out preset) && OwnerLoaded(t.key))
            {
                Parse(preset, t);
            }
        }

        internal static void Load(WeaponCardSettings s)
        {
            tunes.Clear();
            if (s == null || s.buildingTune == null)
            {
                return;
            }
            for (int i = 0; i < s.buildingTune.Count; i++)
            {
                string entry = s.buildingTune[i];
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }
                string[] parts = entry.Split(Sep);
                if (parts.Length < 8)
                {
                    continue;
                }
                DrawerTune t = Get(parts[0]);
                bool.TryParse(parts[1], out t.on);
                bool.TryParse(parts[2], out t.useQueue);
                int q;
                if (int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out q))
                {
                    t.queue = q;
                }
                float x;
                if (float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out x))
                {
                    t.shiftX = x;
                }
                float y;
                if (float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                {
                    t.shiftY = y;
                }
                float z;
                if (float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    t.shiftZ = z;
                }
                bool ex;
                bool.TryParse(parts[7], out ex);
                t.expanded = ex;
            }
        }

        internal static void Save(WeaponCardSettings s)
        {
            if (s == null || tunes.Count == 0)
            {
                return;
            }
            if (s.buildingSeen == null)
            {
                s.buildingSeen = new List<string>();
            }
            if (s.buildingTune == null)
            {
                s.buildingTune = new List<string>();
            }
            s.buildingTune.Clear();
            foreach (DrawerTune t in tunes.Values)
            {
                s.buildingTune.Add(t.key + Sep + t.on + Sep + t.useQueue + Sep + t.queue + Sep
                    + t.shiftX.ToString("F3", CultureInfo.InvariantCulture) + Sep
                    + t.shiftY.ToString("F3", CultureInfo.InvariantCulture) + Sep
                    + t.shiftZ.ToString("F3", CultureInfo.InvariantCulture) + Sep + t.expanded);
            }
        }

        internal static void Save()
        {
            Save(WeaponCardMod.Settings);
        }

        #endregion

        #region install

        internal static void Install(Harmony harmony)
        {
            try
            {
                shapeCardIs = AccessTools.TypeByName("IsoRim.ShapeCards")?.GetMethod("Is", BindingFlags.Public | BindingFlags.Static);
                if (shapeCardIs == null)
                {
                    Log.Warning("[IsoRimWeaponCards] IsoRim.ShapeCards.Is not found; building cards are unavailable.");
                    return;
                }
                Type drawHook = AccessTools.TypeByName("IsoRim.DrawHook");
                drawHookActive = drawHook != null ? AccessTools.Field(drawHook, "active") : null;
                // IsoRim lifts a lying pawn in a building to the top of the building's model/card, and then renders it
                // through its own draw hook - which makes the body skip our fold and stay flat. For a card-form holder
                // that is the wrong model, so IsoRim is made to see no holder at all (same patch Stake Fix had).
                MethodInfo holderOf = AccessTools.TypeByName("IsoRim.Glass")?.GetMethod("HolderOf", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn) }, null);
                if (holderOf != null)
                {
                    harmony.Patch(holderOf, postfix: new HarmonyMethod(typeof(BuildingCards).GetMethod(nameof(HolderOf_Postfix), BindingFlags.NonPublic | BindingFlags.Static)));
                }
                else
                {
                    Log.Warning("[IsoRimWeaponCards] IsoRim.Glass.HolderOf not found; a living pawn on a card building may stay flat.");
                }
                scopePrefix = typeof(BuildingCards).GetMethod(nameof(Scope_Prefix), BindingFlags.NonPublic | BindingFlags.Static);
                scopeFinalizer = typeof(BuildingCards).GetMethod(nameof(Scope_Finalizer), BindingFlags.NonPublic | BindingFlags.Static);
                int scopes = PatchScopes(harmony);
                Log.Message("[IsoRimWeaponCards] building cards ready: " + scopes + " DynamicDrawPhaseAt overrides hooked.");
            }
            catch (Exception e)
            {
                Log.Warning("[IsoRimWeaponCards] building card setup failed: " + e.Message);
            }
        }

        // Every override of Thing.DynamicDrawPhaseAt is a place where a building draws its contents.
        // Assembly.GetTypes() throws ReflectionTypeLoadException when any single type in a mod cannot load (mods with
        // optional dependencies do this all the time), and the old code then skipped the WHOLE assembly - which is how
        // the impaling stake's scope went missing. The partially loaded Type[] is used instead, so the types that do
        // load still get their hooks.
        private static int PatchScopes(Harmony harmony)
        {
            int patched = 0;
            int failedAssemblies = 0;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = SafeTypes(assembly, ref failedAssemblies);
                if (types == null)
                {
                    continue;
                }
                for (int i = 0; i < types.Length; i++)
                {
                    Type t = types[i];
                    if (t == null || !typeof(Thing).IsAssignableFrom(t))
                    {
                        continue;
                    }
                    MethodInfo over;
                    try
                    {
                        over = t.GetMethod("DynamicDrawPhaseAt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    }
                    catch
                    {
                        continue;
                    }
                    if (over == null)
                    {
                        continue;
                    }
                    try
                    {
                        harmony.Patch(over,
                            prefix: new HarmonyMethod(scopePrefix),
                            finalizer: new HarmonyMethod(scopeFinalizer));
                        patched++;
                    }
                    catch
                    {
                    }
                }
            }
            if (failedAssemblies > 0)
            {
                Log.Message("[IsoRimWeaponCards] building cards: " + failedAssemblies
                    + " assemblies had unloadable types; their loadable types were still scanned.");
            }
            return patched;
        }

        private static Type[] SafeTypes(Assembly assembly, ref int failed)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                failed++;
                return e.Types;
            }
            catch
            {
                failed++;
                return null;
            }
        }

        private static void Scope_Prefix(object __instance, out bool __state)
        {
            __state = false;
            try
            {
                Thing thing = __instance as Thing;
                if (thing == null || !thing.Spawned || !IsCard(thing.def))
                {
                    return;
                }
                Push(thing);
                __state = true;
            }
            catch
            {
            }
        }

        // IsoRim must not hand a pawn to its own card/lift path just because it sits inside a card building.
        private static void HolderOf_Postfix(ref Building __result)
        {
            try
            {
                if (__result != null && IsCard(__result.def))
                {
                    __result = null;
                }
            }
            catch
            {
            }
        }

        private static void Scope_Finalizer(bool __state)
        {
            if (__state)
            {
                Pop();
            }
        }

        private static readonly Dictionary<ThingDef, bool> cardCache = new Dictionary<ThingDef, bool>();

        internal static bool IsCard(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }
            bool cached;
            if (cardCache.TryGetValue(def, out cached))
            {
                return cached;
            }
            bool result = false;
            if (shapeCardIs != null)
            {
                try
                {
                    result = (bool)shapeCardIs.Invoke(null, new object[] { def });
                }
                catch
                {
                    result = false;
                }
            }
            cardCache[def] = result;
            return result;
        }

        private static readonly HashSet<string> loggedScopes = new HashSet<string>();

        private static void Push(Thing holder)
        {
            commonAltitude = 0f;
            holdsPawn = false;
            IThingHolderWithDrawnPawn drawnHolder = holder as IThingHolderWithDrawnPawn;
            if (drawnHolder != null)
            {
                holdsPawn = true;
                try
                {
                    commonAltitude = drawnHolder.HeldPawnDrawPos_Y - holder.DrawPos.y;
                }
                catch
                {
                    commonAltitude = 0f;
                }
            }
            upright = View3D.UprightMatrix(holder.DrawPos);
            depth++;
            // Once per building type, so the log shows whether the scope is actually reached in game.
            if (loggedScopes.Add(holder.def.defName))
            {
                Log.Message("[IsoRimWeaponCards] building scope: " + holder.def.defName + " (altitude="
                    + commonAltitude.ToString("F2") + ")");
            }
        }

        private static void Pop()
        {
            if (depth > 0)
            {
                depth--;
            }
        }

        #endregion

        #region applying

        private static bool loggedFirstApply;
        private static bool loggedNoKey;

        // Called for every draw of a card building. Returns true when the building scope handled it (whether or not
        // anything was folded), so the pawn-card logic never runs on a building's draws - that is what keeps a piece
        // from being transformed twice.
        internal static bool TryApply(ref Matrix4x4 matrix, ref int renderQueue)
        {
            if (!Active)
            {
                return false;
            }
            if (!loggedFirstApply)
            {
                loggedFirstApply = true;
                Log.Message("[IsoRimWeaponCards] building draw hook reached (scope active, depth=" + depth + ").");
            }
            // Only buildings that hold a drawn pawn (an impaling stake) need folding. Skipping the rest here is the
            // difference between a smooth view and single-digit TPS once the camera shows a lot of card buildings.
            if (!holdsPawn)
            {
                return true;
            }
            if (!TakeScanBudget())
            {
                return true;
            }
            // Stake Fix's rule: only skip when IsoRim's own draw hook is currently ON. Checking for a DrawHook frame
            // on the stack was too broad - one card render earlier in the chain left frames behind and every later
            // draw of the building (body, corpse, blood) was skipped, which is why they stayed flat.
            if (IsoRimOwns)
            {
                return true;
            }
            string key = CallerKey();
            if (string.IsNullOrEmpty(key))
            {
                if (!loggedNoKey)
                {
                    loggedNoKey = true;
                    Log.Warning("[IsoRimWeaponCards] building draw: no caller key could be read from the stack; "
                        + "the draw is left alone. " + Trace(4));
                }
                return true;
            }
            WeaponCardSettings s = WeaponCardMod.Settings;
            bool isNew = s != null && (s.buildingSeen == null || !s.buildingSeen.Contains(key));
            if (isNew)
            {
                if (s.buildingSeen == null)
                {
                    s.buildingSeen = new List<string>();
                }
                if (s.buildingSeen.Count < MaxSeen)
                {
                    s.buildingSeen.Add(key);
                    Log.Message("[IsoRimWeaponCards] building draw found: " + key + " (listed under 建筑卡片).");
                }
                Get(key);   // newly found methods start switched on, as before
            }
            DrawerTune t;
            if (!tunes.TryGetValue(key, out t) || !t.on)
            {
                return true;
            }
            matrix = Transform(matrix, t);
            if (t.useQueue && renderQueue == 0)
            {
                renderQueue = t.queue;
            }
            return true;
        }

        private static bool TakeScanBudget()
        {
            int frame = Time.frameCount;
            if (scanBudgetFrame != frame)
            {
                scanBudgetFrame = frame;
                scanBudget = ScanBudgetPerFrame;
            }
            if (scanBudget <= 0)
            {
                return false;
            }
            scanBudget--;
            return true;
        }

        private static string Trace(int frames)
        {
            try
            {
                System.Diagnostics.StackTrace trace = new System.Diagnostics.StackTrace(1, false);
                string text = "";
                for (int i = 0; i < trace.FrameCount && i < frames; i++)
                {
                    MethodBase m = trace.GetFrame(i).GetMethod();
                    text += (m != null ? m.DeclaringType?.FullName + "|" + m.Name : "<null>") + " <- ";
                }
                return text;
            }
            catch
            {
                return "<no stack>";
            }
        }

        // Fold one draw into the building's card.
        //   left/up -> world X / world Z, applied before the stand-up so the card's own rotation puts them on its plane
        //   depth   -> after the stand-up, along -camera forward (positive = towards the viewer)
        //   The draw-order altitude is taken off first: the game puts sort-only values in y (a held pawn carries 6.22
        //   while the building sits at 5.488) and the card maths would multiply them by 1/Squash.
        internal static Matrix4x4 Transform(Matrix4x4 world, DrawerTune t)
        {
            Matrix4x4 strip = Matrix4x4.Translate(new Vector3(0f, -commonAltitude, 0f));
            Matrix4x4 r = upright
                * Matrix4x4.Translate(new Vector3(t.shiftX, 0f, t.shiftY))
                * strip
                * world;
            if (Mathf.Abs(t.shiftZ) > 0.0001f)
            {
                try
                {
                    Vector3 forward = View3D.Forward;
                    if (forward.sqrMagnitude > 1e-8f)
                    {
                        r = Matrix4x4.Translate(-forward.normalized * t.shiftZ) * r;
                    }
                }
                catch
                {
                }
            }
            return r;
        }

        // True when the draw already passed through IsoRim's own draw hook. Checked on the stack, not with
        // DrawHook.active: during a card building's draw the held pawn's renderer also sets active, yet most of the
        // building's draws never go through IsoRim's hook - trusting active left the body lying flat.
        internal static bool AlreadyTransformed()
        {
            try
            {
                System.Diagnostics.StackTrace trace = new System.Diagnostics.StackTrace(1, false);
                for (int i = 0; i < trace.FrameCount && i < 12; i++)
                {
                    MethodBase m = trace.GetFrame(i).GetMethod();
                    if (m == null || m.DeclaringType == null)
                    {
                        continue;
                    }
                    if (m.DeclaringType.Namespace == "IsoRim" && m.DeclaringType.Name == "DrawHook")
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        // First frame of this draw that is neither Unity, Harmony, IsoRim plumbing nor this mod.
        internal static string CallerKey()
        {
            try
            {
                System.Diagnostics.StackTrace trace = new System.Diagnostics.StackTrace(1, false);
                for (int i = 0; i < trace.FrameCount && i < 18; i++)
                {
                    MethodBase m = trace.GetFrame(i).GetMethod();
                    if (m == null || m.DeclaringType == null)
                    {
                        continue;
                    }
                    Type type = m.DeclaringType;
                    string name = type.Assembly.GetName().Name;
                    if (name == "UnityStubs" || name.StartsWith("Unity") || name.StartsWith("System")
                        || name == "mscorlib" || name == "netstandard" || name.StartsWith("MonoMod")
                        || name.StartsWith("HarmonyLib") || name == "0Harmony" || name.StartsWith("Waylith")
                        || name == "IsoRimWeaponCards" || name == "IsoRim")
                    {
                        continue;
                    }
                    return type.FullName + "|" + m.Name;
                }
            }
            catch
            {
            }
            return null;
        }

        #endregion
    }
}
