using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using IsoRim;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRimWeaponCards
{
    // One drawing method discovered in game: a comp, render hook or helper of another mod that draws a part of a card
    // pawn (halo, wings, shield, extra weapon...) straight into world space. IsoRim only folds the draws of the methods
    // it knows about, so those parts stay flat on the floor while the card stands up.
    //
    // A discovered method is listed in the settings; when it is switched on, every Graphics.DrawMesh call it makes gets
    // the card transform of the pawn being drawn, plus a render queue and a small shift inside the card's own plane so
    // the part can be sorted in front of or behind the body. Nothing here uses camera depth: on a perspective camera a
    // depth offset changes the part's apparent size and place as the camera turns, which is the drift this mod has been
    // fighting all along.
    internal class DrawerTune
    {
        internal string key;
        internal bool on;
        internal bool useQueue = true;
        internal int queue = 2901;
        internal float shiftX;
        internal float shiftY;
        internal float shiftZ;
        internal bool expanded;

        internal DrawerTune Clone()
        {
            return new DrawerTune
            {
                key = key,
                on = on,
                useQueue = useQueue,
                queue = queue,
                shiftX = shiftX,
                shiftY = shiftY,
                shiftZ = shiftZ,
                expanded = expanded
            };
        }
    }

    internal static class Drawers
    {
        internal const string Tag = "[IsoRimWeaponCards] ";
        private const int MaxSeen = 240;
        private const char Sep = ';';

        // Render queue the parts of a card are drawn in. IsoRim's Api.DrawOnCard clamps a card draw to 2901..3099, so
        // these are the numbers to place a discovered part inside: below the body, or above it.
        internal const int CardQueueLow = 2901;
        internal const int CardQueueHigh = 3099;

        private static readonly Dictionary<string, DrawerTune> tunes = new Dictionary<string, DrawerTune>();
        private static readonly HashSet<MethodBase> patched = new HashSet<MethodBase>();
        private static Harmony harmony;
        private static MethodInfo enterPrefix;
        private static MethodInfo exitFinalizer;
        private static FieldInfo isoCatching;
        private static bool isoCatchingLookedUp;

        [ThreadStatic] private static int depth;
        [ThreadStatic] private static string[] ring;

        // Per-method bookkeeping, so that the hot path never builds a string or walks a stack.
        private static readonly Dictionary<MethodBase, string> keyOfMethod = new Dictionary<MethodBase, string>();
        private static readonly Dictionary<Assembly, string> assemblyNames = new Dictionary<Assembly, string>();

        // Drawer methods that are small enough to be inlined by the JIT, so their entry hook may never run. Only
        // while one of these is still unconfirmed does the stack get walked as a fallback; the first time its entry
        // hook actually fires the key leaves this set and the fallback is off for it for good.
        private static readonly HashSet<string> unverified = new HashSet<string>();
        private static bool scanFallback;

        internal static void Install(Harmony h)
        {
            harmony = h;
            enterPrefix = typeof(Drawers).GetMethod(nameof(Enter), BindingFlags.Static | BindingFlags.NonPublic);
            exitFinalizer = typeof(Drawers).GetMethod(nameof(Exit), BindingFlags.Static | BindingFlags.NonPublic);
            Rebuild();
        }

        #region keys

        internal static string KeyOf(MethodBase m)
        {
            if (m == null || m.DeclaringType == null)
            {
                return null;
            }
            return m.DeclaringType.FullName + "|" + m.Name;
        }

        private static string NameOf(Assembly asm)
        {
            if (asm == null)
            {
                return "";
            }
            string name;
            if (assemblyNames.TryGetValue(asm, out name))
            {
                return name;
            }
            try
            {
                name = asm.GetName().Name;
            }
            catch
            {
                name = "";
            }
            assemblyNames[asm] = name;
            return name;
        }

        // Harmony runs this at the entry of every method the player switched on. It only bumps a counter, so an
        // enabled part costs one dictionary hit per draw; the call stack is never walked for it.
        private static void Enter(MethodBase __originalMethod)
        {
            string key;
            if (!keyOfMethod.TryGetValue(__originalMethod, out key))
            {
                key = KeyOf(__originalMethod);
                if (key != null)
                {
                    keyOfMethod[__originalMethod] = key;
                }
            }
            if (ring == null)
            {
                ring = new string[24];
            }
            if (depth < ring.Length)
            {
                ring[depth] = key;
            }
            depth++;
            if (unverified.Count > 0 && key != null && unverified.Remove(key))
            {
                // The entry hook works for this method, so it no longer needs the stack-walking fallback.
                scanFallback = unverified.Count > 0;
            }
        }

        private static void Exit()
        {
            if (depth > 0)
            {
                depth--;
            }
            if (ring != null && depth < ring.Length)
            {
                ring[depth] = null;
            }
        }

        // The switched-on method whose body is running right now, if any.
        internal static string CurrentKey
        {
            get
            {
                if (depth <= 0 || ring == null)
                {
                    return null;
                }
                return ring[depth - 1];
            }
        }

        #endregion

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
            t = new DrawerTune { key = key };
            tunes[key] = t;
            return t;
        }

        internal static bool Knows(string key)
        {
            return !string.IsNullOrEmpty(key) && tunes.ContainsKey(key);
        }

        internal static void Remove(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                tunes.Remove(key);
            }
        }

        // Read the tuning out of the save file. Called from the settings object as it loads, before Mod.Settings is
        // necessarily assigned, so the settings instance is passed in.
        internal static void Load(WeaponCardSettings s)
        {
            tunes.Clear();
            if (s == null)
            {
                return;
            }
            // Old saves only knew "seen" and "on"; build the rest from those, then let the tuning entries win.
            if (s.drawerSeen != null)
            {
                for (int i = 0; i < s.drawerSeen.Count; i++)
                {
                    string key = s.drawerSeen[i];
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }
                    DrawerTune t = Get(key);
                    if (s.drawerOn != null && s.drawerOn.Contains(key))
                    {
                        t.on = true;
                    }
                }
            }
            if (s.drawerTune != null)
            {
                for (int i = 0; i < s.drawerTune.Count; i++)
                {
                    string entry = s.drawerTune[i];
                    if (string.IsNullOrEmpty(entry))
                    {
                        continue;
                    }
                    string[] parts = entry.Split(Sep);
                    if (parts.Length < 7)
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
                        if (parts.Length >= 8)
                        {
                            t.shiftY = y;
                        }
                        else
                        {
                            // Older entry: that column was what the player tuned on the "plane up" slider, but it
                            // moves the part along the camera axis, so it becomes the depth value and the new
                            // "plane up" starts at 0.
                            t.shiftZ = y;
                        }
                    }
                    if (parts.Length >= 8)
                    {
                        // key;on;useQueue;queue;x;up;depth;expanded
                        float z;
                        if (float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                        {
                            t.shiftZ = z;
                        }
                        bool ex2;
                        bool.TryParse(parts[7], out ex2);
                        t.expanded = ex2;
                    }
                    else
                    {
                        bool ex;
                        bool.TryParse(parts[6], out ex);
                        t.expanded = ex;
                    }
                }
            }
            // No hooking here: Load runs while the settings are scribed (which also happens when a new method is
            // detected during rendering). Hooks are installed once at startup and on each settings toggle.
            RefreshActive();
        }

        internal static void Save()
        {
            Save(WeaponCardMod.Settings);
        }

        // Write the tuning back into the settings object before it is scribed.
        internal static void Save(WeaponCardSettings s)
        {
            if (s == null || tunes.Count == 0)
            {
                return;
            }
            if (s.drawerSeen == null)
            {
                s.drawerSeen = new List<string>();
            }
            if (s.drawerOn == null)
            {
                s.drawerOn = new List<string>();
            }
            if (s.drawerTune == null)
            {
                s.drawerTune = new List<string>();
            }
            s.drawerTune.Clear();
            s.drawerOn.Clear();
            foreach (DrawerTune t in tunes.Values)
            {
                if (t.on)
                {
                    s.drawerOn.Add(t.key);
                }
                s.drawerTune.Add(t.key + Sep + t.on + Sep + t.useQueue + Sep + t.queue + Sep
                    + t.shiftX.ToString("F3", CultureInfo.InvariantCulture) + Sep
                    + t.shiftY.ToString("F3", CultureInfo.InvariantCulture) + Sep
                    + t.shiftZ.ToString("F3", CultureInfo.InvariantCulture) + Sep + t.expanded);
            }
        }

        // Drop a method that is no longer known or switched on, and hook the new ones.
        internal static void Rebuild()
        {
            if (harmony == null)
            {
                return;
            }
            Rehook();
        }

        private static void Rehook()
        {
            try
            {
                RefreshActive();
                HashSet<MethodBase> want = new HashSet<MethodBase>();
                unverified.Clear();
                foreach (DrawerTune t in tunes.Values)
                {
                    if (!t.on)
                    {
                        continue;
                    }
                    foreach (MethodBase m in Resolve(t.key))
                    {
                        if (OwnedByIsoRim(m))
                        {
                            Log.WarningOnce(Tag + "IsoRim already folds the draws of " + t.key + ", so it is skipped.", 0x5E001);
                            continue;
                        }
                        want.Add(m);
                        keyOfMethod[m] = t.key;
                        if (MayBeInlined(m))
                        {
                            unverified.Add(t.key);
                        }
                    }
                }
                List<MethodBase> drop = new List<MethodBase>();
                foreach (MethodBase m in patched)
                {
                    if (!want.Contains(m))
                    {
                        drop.Add(m);
                    }
                }
                for (int i = 0; i < drop.Count; i++)
                {
                    try
                    {
                        harmony.Unpatch(drop[i], enterPrefix);
                        patched.Remove(drop[i]);
                    }
                    catch (Exception e)
                    {
                        Log.Warning(Tag + "could not unhook " + KeyOf(drop[i]) + ": " + e.Message);
                    }
                }
                foreach (MethodBase m in want)
                {
                    if (patched.Contains(m))
                    {
                        continue;
                    }
                    try
                    {
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(enterPrefix),
                            finalizer: new HarmonyMethod(exitFinalizer));
                        patched.Add(m);
                    }
                    catch (Exception e)
                    {
                        Log.Warning(Tag + "could not hook " + KeyOf(m) + ": " + e.Message);
                    }
                }
                scanFallback = unverified.Count > 0;
                // Diagnostic: which methods really have an entry hook after a rebuild. This is what tells whether a
                // newly ticked drawing method (an add-on's halo Draw, say) got hooked at all.
                List<string> wantKeys = new List<string>();
                foreach (MethodBase m in want)
                {
                    wantKeys.Add(KeyOf(m));
                }
                Log.Message(Tag + "drawer hooks: " + patched.Count + " active / " + want.Count + " wanted"
                    + (unverified.Count > 0 ? " (stack fallback on)" : "") + ": " + string.Join(", ", wantKeys.ToArray()));
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "drawer hooks failed: " + e.Message);
            }
        }

        // A method small enough that the JIT may inline it into its caller: its entry hook could then never run, so
        // its draws need the stack-walking fallback until one of its draws proves the hook works.
        private static bool MayBeInlined(MethodBase m)
        {
            try
            {
                MethodBody body = m.GetMethodBody();
                if (body == null)
                {
                    return false;
                }
                byte[] il = body.GetILAsByteArray();
                return il != null && il.Length < 120;
            }
            catch
            {
                return true;
            }
        }

        // True while some switched-on method still needs the stack-walking fallback.
        internal static bool ScanFallbackNeeded
        {
            get { return scanFallback; }
        }

        internal static int EnabledCount
        {
            get
            {
                int n = 0;
                foreach (DrawerTune t in tunes.Values)
                {
                    if (t.on)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        // Starting values, so a row can be put back after experimenting. Built-in drawers register theirs (with the
        // queue and offset that suit the part); everything else falls back to "card bottom, no offset".
        private static readonly Dictionary<string, DrawerTune> defaults = new Dictionary<string, DrawerTune>();

        internal static void SetDefault(string key, int queue, float shiftX, float shiftY, float shiftZ)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }
            defaults[key] = new DrawerTune
            {
                key = key,
                useQueue = true,
                queue = queue,
                shiftX = shiftX,
                shiftY = shiftY,
                shiftZ = shiftZ
            };
        }

        internal static void ResetToDefault(DrawerTune t)
        {
            if (t == null)
            {
                return;
            }
            DrawerTune d;
            if (defaults.TryGetValue(t.key, out d))
            {
                t.useQueue = d.useQueue;
                t.queue = d.queue;
                t.shiftX = d.shiftX;
                t.shiftY = d.shiftY;
                t.shiftZ = d.shiftZ;
                return;
            }
            t.useQueue = true;
            t.queue = CardQueueLow;
            t.shiftX = 0f;
            t.shiftY = 0f;
            t.shiftZ = 0f;
        }

        // IsoRim's own catch list (FigureWeapons.catching): the draws of those methods are already folded into the card.
        // Multiplying the card transform on top of that draws the part twice as large, which is what happened to the
        // ratkin anomaly part someone added.
        private static bool OwnedByIsoRim(MethodBase m)
        {
            if (m == null)
            {
                return false;
            }
            try
            {
                if (!isoCatchingLookedUp)
                {
                    isoCatchingLookedUp = true;
                    isoCatching = typeof(FigureWeapons).GetField("catching", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                }
                if (isoCatching == null)
                {
                    return false;
                }
                object set = isoCatching.GetValue(null);
                System.Collections.IEnumerable items = set as System.Collections.IEnumerable;
                if (items == null)
                {
                    return false;
                }
                foreach (object item in items)
                {
                    MethodBase other = item as MethodBase;
                    if (other != null && other == m)
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

        private static IEnumerable<MethodBase> Resolve(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                yield break;
            }
            int bar = key.LastIndexOf('|');
            if (bar <= 0 || bar >= key.Length - 1)
            {
                yield break;
            }
            string typeName = key.Substring(0, bar);
            string methodName = key.Substring(bar + 1);
            Type type = FindType(typeName);
            if (type == null)
            {
                yield break;
            }
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            }
            catch
            {
                yield break;
            }
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == methodName)
                {
                    yield return methods[i];
                }
            }
        }

        private static Type FindType(string name)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly a = assemblies[i];
                if (a.IsDynamic)
                {
                    continue;
                }
                try
                {
                    Type t = a.GetType(name, false);
                    if (t != null)
                    {
                        return t;
                    }
                }
                catch
                {
                }
            }
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly a = assemblies[i];
                if (a.IsDynamic)
                {
                    continue;
                }
                Type[] all;
                try
                {
                    all = a.GetTypes();
                }
                catch
                {
                    continue;
                }
                for (int j = 0; j < all.Length; j++)
                {
                    if (all[j].FullName == name || all[j].Name == name)
                    {
                        return all[j];
                    }
                }
            }
            return null;
        }

        #endregion

        #region applying

        // True when at least one method is switched on (then the auto-detect scan is also allowed to apply).
        private static bool hasActive;

        internal static bool HasActive
        {
            get { return hasActive; }
        }

        private static void RefreshActive()
        {
            hasActive = false;
            foreach (DrawerTune t in tunes.Values)
            {
                if (t.on)
                {
                    hasActive = true;
                    return;
                }
            }
        }

        // Called for every Graphics.DrawMesh of the game. If a switched-on method is running, the draw is folded into
        // the card of the pawn being drawn, sorted with the queue the player picked and shifted inside the card plane.
        //
        // Normally the method entry counter tells us which method is drawing, which costs nothing. Very small private
        // helper methods (the isekai halo among them) can be inlined by the JIT, so the entry hook never runs; for
        // those the stack is scanned, but only for draws that are already close to a card pawn, so the cost stays
        // bounded by the number of part draws near a pawn instead of every draw on the map.
        internal static bool Apply(ref Matrix4x4 matrix, ref int renderQueue)
        {
            string key = CurrentKey;
            if (key != null)
            {
                DrawerTune fast;
                if (!tunes.TryGetValue(key, out fast) || !fast.on)
                {
                    return false;
                }
                return Fold(ref matrix, ref renderQueue, fast);
            }
            if (!hasActive || !scanFallback || !ScanFallbackEnabled)
            {
                return false;
            }
            Vector4 corner = matrix.GetColumn(3);
            Vector3 world = new Vector3(corner.x, corner.y, corner.z);
            // The fallback only makes sense right on top of a card pawn (the parts it exists for are drawn at the
            // pawn's own spot), and only a few times per frame.
            if (!Patch.NearAnyCard(world, 1.2f) || !TakeScanBudget(false))
            {
                return false;
            }
            string scanned;
            bool already;
            if (!Scan(out scanned, out already) || already || string.IsNullOrEmpty(scanned))
            {
                return false;
            }
            DrawerTune t;
            if (!tunes.TryGetValue(scanned, out t) || !t.on)
            {
                return false;
            }
            return Fold(ref matrix, ref renderQueue, t);
        }

        private static bool ScanFallbackEnabled        {
            get
            {
                WeaponCardSettings s = WeaponCardMod.Settings;
                return s == null || s.drawerScanFallback;
            }
        }

        // Stack walks are the expensive part, so each frame gets a fixed number of them. Even when one is skipped
        // the part is still drawn correctly on the next frame's draw of the same method.
        private const int ScansPerFrameParts = 2;
        private const int ScansPerFrameDetect = 1;

        [ThreadStatic] private static int partScanLeft;
        [ThreadStatic] private static int detectScanLeft;
        [ThreadStatic] private static int scanBudgetFrame = -1;

        private static bool TakeScanBudget(bool detect)
        {
            int frame = Time.frameCount;
            if (scanBudgetFrame != frame)
            {
                scanBudgetFrame = frame;
                partScanLeft = ScansPerFrameParts;
                detectScanLeft = ScansPerFrameDetect;
            }
            if (detect)
            {
                if (detectScanLeft <= 0)
                {
                    return false;
                }
                detectScanLeft--;
                return true;
            }
            if (partScanLeft <= 0)
            {
                return false;
            }
            partScanLeft--;
            return true;
        }

        private static readonly HashSet<string> noCardLogged = new HashSet<string>();

        // Diagnostic: a ticked method drew, but there was no card pawn within reach to attach it to.
        private static void LogNoCardOnce(string key, Vector3 at)
        {
            if (noCardLogged.Add(key))
            {
                Log.Message(Tag + "no card pawn within 2.5 cells of " + key + " at ("
                    + at.x.ToString("F1") + ", " + at.z.ToString("F1") + ")");
            }
        }

        private static bool Fold(ref Matrix4x4 matrix, ref int renderQueue, DrawerTune t)
        {
            // A mote that is not attached to anything (a bullet, smoke, a blood spray) must never be folded: it does
            // not move with the pawn, so it would be dragged onto the card as a stretched smear. Only an attached
            // mote - the vanilla fishing rod is one - belongs on the card.
            Mote mote = Patch.CurrentMote;
            if (mote != null && !mote.link1.Linked)
            {
                return false;
            }
            Matrix4x4 xform;
            Matrix4x4 extra;
            Pawn pawn;
            if (!Patch.CardMatrices(out xform, out extra, out pawn))
            {
                // Drawn outside PawnRenderer.RenderPawnAt. First choice: the pawn Pawn.DrawAt is currently drawing -
                // on IsoRimZ's upper floors the pawn never enters the frame list, so asking the list alone left the
                // part lying on the floor there.
                Pawn direct = Patch.DrawAtPawn;
                if (direct != null && Patch.CardMatricesFor(direct, out xform, out extra))
                {
                    pawn = direct;
                }
                else
                {
                    // Otherwise take the card of the closest pawn drawn this frame (or the one before).
                    Vector4 a0 = matrix.GetColumn(3);
                    Vector3 at3 = new Vector3(a0.x, a0.y, a0.z);
                    if (direct != null)
                    {
                        // There IS a pawn being drawn here, but it is not in IsoRim's card cache this frame.
                        LogNoCardOnce(t.key + " (drawAt pawn of " + direct.LabelShort + " is not a card)", at3);
                    }
                    if (!Patch.NearestCardInFrame(at3, 2.5f, out pawn, out xform, out extra))
                    {
                        LogNoCardOnce(t.key, at3);
                        return false;
                    }
                }
            }
            // Flatten a draw that sits clearly above the ground to the pawn's ground height first. A world height is
            // turned by the card rotation into a sideways shift AND a camera-depth shift; on a perspective camera
            // that depth part moves the part as the camera turns, which is exactly the drift this mod must not
            // introduce. Parts drawn at ground level (shields, carried items) keep their tiny height as they are.
            // Both are game-space here: DrawMeshCorePrefix runs before IsoRim's floor mapping (Priority.First).
            Vector4 at = matrix.GetColumn(3);
            if (pawn != null && at.y - pawn.DrawPos.y > 0.3f)
            {
                at.y = pawn.DrawPos.y;
                matrix.SetColumn(3, at);
            }
            // Left = camera right, depth = towards/away from the viewer (both after the card transform).
            // Vertical = a translation along world Z applied BEFORE the card transform, with no compensation: the
            // card's own rotation (Euler(pitch - 90, yaw)) turns that axis into the card's vertical direction, which
            // is how a card's contents are placed. This was picked by eye out of nine candidate directions and beats
            // every derivation tried before it.
            Vector3 lateral = Patch.CardShift(t.shiftX, t.shiftZ);
            matrix = Matrix4x4.Translate(lateral)
                * extra * xform
                * Matrix4x4.Translate(new Vector3(0f, 0f, t.shiftY))
                * matrix;
            if (t.useQueue)
            {
                renderQueue = t.queue;
            }
            return true;
        }

        #endregion

        #region source mod

        private static readonly Dictionary<string, string> modNames = new Dictionary<string, string>();

        // The mod that owns the method, for the settings list ("鼠族异象", "原版", ...). Assemblies of the base game
        // belong to no mod, so they are shown as the game itself.
        internal static string ModOf(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            string cached;
            if (modNames.TryGetValue(key, out cached))
            {
                return cached;
            }
            string name = null;
            try
            {
                int bar = key.LastIndexOf('|');
                if (bar > 0)
                {
                    Type type = FindType(key.Substring(0, bar));
                    if (type != null)
                    {
                        Assembly asm = type.Assembly;
                        List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
                        if (mods != null)
                        {
                            for (int i = 0; i < mods.Count; i++)
                            {
                                ModContentPack mod = mods[i];
                                try
                                {
                                    if (mod != null && mod.assemblies != null && mod.assemblies.loadedAssemblies != null
                                        && mod.assemblies.loadedAssemblies.Contains(asm))
                                    {
                                        name = mod.Name;
                                        break;
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                        if (string.IsNullOrEmpty(name))
                        {
                            string asmName = NameOf(asm);
                            name = asmName == "Assembly-CSharp"
                                ? "IsoRimWeaponCards.Vanilla".Translate().ToString()
                                : asmName;
                        }
                    }
                }
            }
            catch
            {
                name = null;
            }
            if (name == null)
            {
                name = "";
            }
            modNames[key] = name;
            return name;
        }

        #endregion

        #region detecting

        [ThreadStatic] private static int sweepUntilFrame = -1;
        private static readonly HashSet<string> swept = new HashSet<string>();
        private static readonly HashSet<string> reportedFolded = new HashSet<string>();

        internal static bool Sweeping
        {
            get { return Time.frameCount <= sweepUntilFrame; }
        }

        // Log every draw call of the next few frames, whatever it belongs to, so a part that is never listed can
        // still be identified by name in the log.
        internal static void Sweep(int frames)
        {
            sweepUntilFrame = Time.frameCount + Mathf.Max(1, frames);
            swept.Clear();
            Log.Message(Tag + "sweep: listing every Graphics.DrawMesh caller of the next " + frames + " frames.");
        }

        // Called for every Graphics.DrawMesh of the game while auto-collect is on. A draw that belongs to a card pawn,
        // is close to it, and did not pass through IsoRim's own draw hook is a part that would stay on the floor: its
        // caller is recorded for the player to switch on.
        internal static void MaybeDetect(Matrix4x4 matrix, Material material, int layer)
        {
            WeaponCardSettings s = WeaponCardMod.Settings;
            if (s == null)
            {
                return;
            }
            bool sweep = Sweeping;
            if (!s.autoCollect && !sweep)
            {
                return;
            }
            try
            {
                Vector4 at = matrix.GetColumn(3);
                Vector3 world = new Vector3(at.x, at.y, at.z);
                Pawn pawn = Patch.CurrentCardPawn();
                Matrix4x4 xform;
                Matrix4x4 extra;
                bool haveCard = Patch.CardMatrices(out xform, out extra);
                if (!haveCard)
                {
                    haveCard = Patch.NearestCardInFrame(world, sweep ? 4f : 1.6f, out pawn, out xform, out extra);
                }
                if (!haveCard)
                {
                    return;
                }
                if (!TakeScanBudget(true))
                {
                    return;
                }
                float dist = -1f;
                if (pawn != null)
                {
                    Vector3 d = pawn.DrawPos;
                    float dx = at.x - d.x;
                    float dz = at.z - d.z;
                    dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (!sweep && dist > 2.5f)
                    {
                        return;
                    }
                }
                string key;
                bool already;
                if (!Scan(out key, out already) || string.IsNullOrEmpty(key))
                {
                    return;
                }
                int queue = material != null ? material.renderQueue : -1;
                if (sweep && swept.Add(key))
                {
                    Log.Message(Tag + "sweep: " + key + " at (" + at.x.ToString("F2") + ", " + at.z.ToString("F2")
                        + ") dist " + dist.ToString("F2") + " layer " + layer + " queue " + queue
                        + (already ? " (already folded into the card by IsoRim)" : " (not folded by IsoRim)"));
                }
                if (already)
                {
                    // IsoRim already folds this draw into the card. Multiplying again would double it, which is what
                    // happened to one of the ratkin anomaly parts, so it is only reported once.
                    if (s.autoCollect && reportedFolded.Add(key))
                    {
                        Log.Message(Tag + "skip " + key + ": IsoRim already folds its draws into the card.");
                    }
                    return;
                }
                if (s.drawerSeen == null)
                {
                    s.drawerSeen = new List<string>();
                }
                if (s.drawerSeen.Contains(key) || s.drawerSeen.Count >= MaxSeen)
                {
                    return;
                }
                s.drawerSeen.Add(key);
                Get(key);
                Save();
                try
                {
                    s.Write();
                }
                catch
                {
                }
                Log.Message(Tag + "found " + key + " drawing at (" + at.x.ToString("F2") + ", " + at.z.ToString("F2")
                    + ") dist " + dist.ToString("F2") + " layer " + layer + " queue " + queue
                    + " while drawing card pawn " + (pawn != null ? pawn.LabelShort : "?")
                    + ". It is listed in the mod settings under the drawing methods.");
            }
            catch
            {
            }
        }

        // First frame of this draw that is neither Unity, Harmony, IsoRim plumbing or this mod. IsoRim's draw hook in
        // the stack means the draw was already folded into a card - multiplying it again would double it.
        private static bool Scan(out string key, out bool already)
        {
            key = null;
            already = false;
            try
            {
                StackTrace trace = new StackTrace(1, false);
                int frames = trace.FrameCount;
                for (int i = 0; i < frames && i < 18; i++)
                {
                    MethodBase m = trace.GetFrame(i).GetMethod();
                    if (m == null || m.DeclaringType == null)
                    {
                        continue;
                    }
                    Type type = m.DeclaringType;
                    Assembly asm = type.Assembly;
                    string name = NameOf(asm);
                    if (name == "UnityStubs" || name.StartsWith("Unity") || name.StartsWith("System")
                        || name == "mscorlib" || name == "netstandard" || name.StartsWith("MonoMod")
                        || name.StartsWith("HarmonyLib") || name == "0Harmony" || name.StartsWith("Waylith")
                        || name == "IsoRimWeaponCards")
                    {
                        continue;
                    }
                    if (name == "IsoRim")
                    {
                        if (type.Name == "DrawHook")
                        {
                            already = true;
                            return true;
                        }
                        continue;
                    }
                    string candidate = type.FullName + "|" + m.Name;
                    if (candidate == "RimWorld.PawnRenderer|RenderPawnAt" || candidate == "Verse.PawnRenderer|RenderPawnAt")
                    {
                        continue;
                    }
                    key = candidate;
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        #endregion
    }
}
