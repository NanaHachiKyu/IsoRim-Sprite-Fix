using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using IsoRim;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRimWeaponCards
{
    // IsoRim already folds a card pawn's weapon into the card (DrawHook). What it cannot fix is the angle the
    // "weapon back" mods (SYS.dll and the copies shipped by the Ratkin / Miho / Epona mods) pass for an idle pawn:
    // they use the two fixed carried angles and pick them from pawn.Rotation, so as soon as the camera turns the
    // barrel stops matching the sprite. This mod recomputes that draw exactly like the game's own DrawCarriedWeapon,
    // but from the facing the renderer actually passed, and puts the hand sprites of Show Me Your Hands on the card.
    //
    // Parts other mods draw themselves (halos, wings, shields, extra weapons) are found at runtime by Drawers: their
    // draw calls never pass through IsoRim, so they stay flat on the floor while the card stands up.
    public class WeaponCardSettings : ModSettings
    {
        public bool enabled = true;
        public float handHeight = 0f;
        public bool hideFromBehind = true;
        public bool replicateVanilla = true;
        public bool autoCollect;
        public bool drawerScanFallback;
        // Game-side entry: a button in the play settings row that opens this mod's floating settings window.
        public bool showEntryButton = true;
        // Where the floating window was left (negative = never moved, centre it).
        public float windowX = -1f;
        public float windowY = -1f;
        // Building cards (立牌建筑的内容跟随卡片): see BuildingCards.
        public bool buildingEnabled = true;
        public float buildingLeft;
        public float buildingUp;
        public int buildingQueue = 2901;
        public bool buildingAutoCollect;
        public List<string> buildingSeen = new List<string>();
        public List<string> buildingTune = new List<string>();
        public List<string> drawerSeen = new List<string>();
        public List<string> drawerOn = new List<string>();
        public List<string> drawerTune = new List<string>();
        public float backDepth = 0.06f;

        // The drawers this mod knows how to place, with the assembly that owns them, plus starting values:
        // key, assembly prefix, plane right, plane up, depth (towards the camera), render queue.
        //   Isekai Leveling draws its ascension halo (AuraSystem.DrawHaloRing) from a Pawn.DrawAt postfix, i.e. after
        //   IsoRim has finished the card, as a flat world-space quad at the pawn's head height. Its head height has to
        //   be rebuilt on the card, so it starts lifted on the plane; the depth stays 0.
        private static readonly string[][] Builtins = new[]
        {
            new[] { "Epona.Shield|DrawShield", "Epona" },
            new[] { "NewRatkin.CompExtraDrawer|DrawExtra", "NewRatkin" },
            new[] { "NewRatkin.CompExtraDrawer|CompDrawWornExtras", "NewRatkin" },
            new[] { "IsekaiLeveling.Effects.AuraSystem|DrawHaloRing", "IsekaiLeveling", "0", "1.55", "0", "3000" }
        };

        private void SeedBuiltinDrawers()
        {
            try
            {
                foreach (string[] entry in Builtins)
                {
                    string key = entry[0];
                    string asmPrefix = entry[1];
                    float defX = 0f;
                    float defUp = 0f;
                    float defDepth = 0f;
                    int defQueue = Drawers.CardQueueLow;
                    if (entry.Length > 5)
                    {
                        float x;
                        if (float.TryParse(entry[2], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out x))
                        {
                            defX = x;
                        }
                        float up;
                        if (float.TryParse(entry[3], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out up))
                        {
                            defUp = up;
                        }
                        float depth;
                        if (float.TryParse(entry[4], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out depth))
                        {
                            defDepth = depth;
                        }
                        int queue;
                        if (int.TryParse(entry[5], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out queue))
                        {
                            defQueue = queue;
                        }
                    }
                    // Registered even when the row already exists, so "reset to default" works on old saves too.
                    Drawers.SetDefault(key, defQueue, defX, defUp, defDepth);
                    if (drawerSeen.Contains(key) || drawerOn.Contains(key))
                    {
                        // Already recorded once: leave the player's on/off choice alone.
                        continue;
                    }
                    bool loaded = false;
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        string name = assembly.GetName().Name;
                        if (name.StartsWith(asmPrefix))
                        {
                            loaded = true;
                            break;
                        }
                    }
                    if (!loaded)
                    {
                        continue;
                    }
                    drawerSeen.Add(key);
                    drawerOn.Add(key);
                    DrawerTune tune = Drawers.Get(key);
                    tune.on = true;
                    tune.useQueue = true;
                    tune.queue = defQueue;
                    tune.shiftX = defX;
                    tune.shiftY = defUp;
                    tune.shiftZ = defDepth;
                }
            }
            catch
            {
            }
        }
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Drawers.Save(this);
                BuildingCards.Save(this);
            }
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref handHeight, "handHeight", 0f);
            Scribe_Values.Look(ref hideFromBehind, "hideFromBehind", true);
            Scribe_Values.Look(ref replicateVanilla, "replicateVanilla", true);
            Scribe_Values.Look(ref autoCollect, "autoCollect", false);
            Scribe_Values.Look(ref drawerScanFallback, "drawerScanFallback", false);
            Scribe_Values.Look(ref showEntryButton, "showEntryButton", true);
            Scribe_Values.Look(ref windowX, "windowX", -1f);
            Scribe_Values.Look(ref windowY, "windowY", -1f);
            Scribe_Values.Look(ref buildingEnabled, "buildingEnabled", true);
            Scribe_Values.Look(ref buildingLeft, "buildingLeft", 0f);
            Scribe_Values.Look(ref buildingUp, "buildingUp", 0f);
            Scribe_Values.Look(ref buildingQueue, "buildingQueue", 2901);
            Scribe_Values.Look(ref buildingAutoCollect, "buildingAutoCollect", false);
            Scribe_Collections.Look(ref buildingSeen, "buildingSeen", LookMode.Value);
            Scribe_Collections.Look(ref buildingTune, "buildingTune", LookMode.Value);
            Scribe_Collections.Look(ref drawerSeen, "drawerSeen", LookMode.Value);
            Scribe_Collections.Look(ref drawerOn, "drawerOn", LookMode.Value);
            Scribe_Collections.Look(ref drawerTune, "drawerTune", LookMode.Value);
            if (drawerSeen == null)
            {
                drawerSeen = new List<string>();
            }
            if (drawerOn == null)
            {
                drawerOn = new List<string>();
            }
            if (drawerTune == null)
            {
                drawerTune = new List<string>();
            }
            if (buildingSeen == null)
            {
                buildingSeen = new List<string>();
            }
            if (buildingTune == null)
            {
                buildingTune = new List<string>();
            }
            SeedBuiltinDrawers();
            Drawers.Load(this);
            BuildingCards.Load(this);
            Scribe_Values.Look(ref backDepth, "backDepth", 0.06f);
        }
    }

    public class WeaponCardMod : Mod
    {
        public static WeaponCardSettings Settings;
        public static WeaponCardMod Instance;
        private Vector2 drawerScroll;
        private Vector2 buildingScroll;
        private int tab;
        // Two nested scroll panes per tab: the whole settings page (below the tabs) and the found-methods list
        // inside it. pageHeight is the height measured while drawing the previous frame.
        private readonly Vector2[] pageScroll = new Vector2[3];
        private readonly float[] pageHeight = new float[3];
        private readonly Rect[] innerArea = new Rect[3];
        private readonly bool[] innerScrollable = new bool[3];
        // Height of the page currently being drawn, so the found-methods list can fill the rest of it.
        private float pageBodyHeight;
        // In-place number editing: id of the field being edited, its text, and whether it still needs focus.
        private static string editId;
        private static string editText;
        private static bool editNeedsFocus;
        private const float PageBarWidth = 16f;

        public WeaponCardMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<WeaponCardSettings>();
        }

        public override string SettingsCategory()
        {
            return "IsoRimWeaponCards.Category".Translate().ToString();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Tabs at the top: pawn cards (weapons + extra parts) and building cards.
            Rect bar = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            float tabWidth = 140f;
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, tabWidth, 26f),
                (tab == 0 ? "● " : "") + "IsoRimWeaponCards.Tab.Pawn".Translate().ToString()))
            {
                tab = 0;
            }
            if (Widgets.ButtonText(new Rect(bar.x + tabWidth + 6f, bar.y, tabWidth, 26f),
                (tab == 1 ? "● " : "") + "IsoRimWeaponCards.Tab.Building".Translate().ToString()))
            {
                tab = 1;
            }
            if (Widgets.ButtonText(new Rect(bar.x + (tabWidth + 6f) * 2f, bar.y, tabWidth, 26f),
                (tab == 2 ? "● " : "") + "IsoRimWeaponCards.Tab.Controls".Translate().ToString()))
            {
                tab = 2;
            }
            Rect body = new Rect(inRect.x, inRect.y + 34f, inRect.width, inRect.height - 34f);
            DrawPage(body, tab);
        }

        // The whole tab scrolls as one page. The page height is the measured height of the previous frame (every
        // control is laid out each frame, so it is exact from the second frame on). Without this the settings below
        // the found-methods list were pushed out of the game's fixed-size mod settings dialog and never drawn.
        private void DrawPage(Rect body, int tab)
        {
            int idx = Mathf.Clamp(tab, 0, 2);
            pageBodyHeight = body.height;
            Vector2 mouse = Event.current.mousePosition;
            float contentHeight = Mathf.Max(pageHeight[idx], body.height);
            float maxScroll = Mathf.Max(0f, contentHeight - body.height);
            bool hasBar = maxScroll > 0.5f;

            // The found-methods list is drawn inside this page, so when the mouse is over it the wheel must scroll
            // the list only (otherwise both panes would move at once).
            Rect innerScreen = new Rect(body.x + innerArea[idx].x, body.y + innerArea[idx].y - pageScroll[idx].y,
                innerArea[idx].width, innerArea[idx].height);
            bool wheelOnInner = innerScrollable[idx] && innerScreen.Contains(mouse);
            if (Event.current.type == EventType.scrollWheel && hasBar && body.Contains(mouse) && !wheelOnInner)
            {
                pageScroll[idx].y = Mathf.Clamp(pageScroll[idx].y + Event.current.delta.y * 20f, 0f, maxScroll);
                Event.current.Use();
            }
            pageScroll[idx].y = Mathf.Clamp(pageScroll[idx].y, 0f, maxScroll);

            // Cleared every frame; DrawPawnTab / DrawBuildingTab register it again when they draw the list.
            innerArea[idx] = Rect.zero;
            innerScrollable[idx] = false;

            Widgets.BeginGroup(body);
            Rect contentRect = new Rect(0f, -pageScroll[idx].y, body.width - (hasBar ? PageBarWidth : 0f), contentHeight);
            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.Begin(contentRect);
            if (idx == 1)
            {
                DrawBuildingTab(list);
            }
            else if (idx == 2)
            {
                DrawControlsTab(list);
            }
            else
            {
                DrawPawnTab(list);
            }
            float measured = list.CurHeight;
            list.End();
            Widgets.EndGroup();
            pageHeight[idx] = measured + 6f;

            if (hasBar)
            {
                Rect bar = new Rect(body.xMax - PageBarWidth, body.y, PageBarWidth, body.height);
                // GUI.VerticalScrollbar wants the handle SIZE as a 0..1 fraction, not a length: multiplying by maxScroll made the handle fill the track and the view could not reach the end.
                float thumb = Mathf.Clamp(body.height / contentHeight, 0.02f, 1f);
                pageScroll[idx].y = Mathf.Clamp(
                    GUI.VerticalScrollbar(bar, pageScroll[idx].y, thumb, 0f, maxScroll), 0f, maxScroll);
            }
        }

        // One nested scroll list: takes the wheel only while the mouse is over it, clips the rows to the box and
        // draws its own scrollbar on the right. drawRows is called inside the clipped group; row coordinates start
        // at 0 and must be offset by "offset" (the current scroll).
        private void DrawListScroll(Rect area, float viewHeight, ref Vector2 scroll, int idx, Action<float, float> drawRows)
        {
            float maxScroll = Mathf.Max(0f, viewHeight - area.height);
            innerArea[idx] = area;
            innerScrollable[idx] = maxScroll > 0.5f;
            if (Event.current.type == EventType.scrollWheel && maxScroll > 0.5f && Mouse.IsOver(area))
            {
                scroll.y = Mathf.Clamp(scroll.y + Event.current.delta.y * 20f, 0f, maxScroll);
                Event.current.Use();
            }
            scroll.y = Mathf.Clamp(scroll.y, 0f, maxScroll);
            // 20 = room for the scrollbar plus a small gap, same as the old Widgets.BeginScrollView layout.
            float rowWidth = area.width - 20f;
            Widgets.BeginGroup(area);
            drawRows(rowWidth, -scroll.y);
            Widgets.EndGroup();
            if (maxScroll > 0.5f)
            {
                Rect bar = new Rect(area.x + area.width - PageBarWidth, area.y, PageBarWidth, area.height);
                float thumb = Mathf.Clamp(area.height / viewHeight, 0.02f, 1f);   // fraction, not length (see DrawPage)
                scroll.y = Mathf.Clamp(GUI.VerticalScrollbar(bar, scroll.y, thumb, 0f, maxScroll), 0f, maxScroll);
            }
        }

        private void DrawPawnTab(Listing_Standard list)
        {
            list.CheckboxLabeled("IsoRimWeaponCards.Setting.Weapon".Translate(), ref Settings.enabled);
            if (Prefs.DevMode)
            {
                list.Label("IsoRimWeaponCards.Setting.WeaponHeight".Translate(Settings.handHeight.ToString("F2")), -1f);
                Settings.handHeight = Mathf.Round(list.Slider(Settings.handHeight, 0f, 1.6f) * 100f) / 100f;
            }
            list.CheckboxLabeled("IsoRimWeaponCards.Setting.HideFromBehind".Translate(), ref Settings.hideFromBehind);
            if (Prefs.DevMode && !Settings.hideFromBehind)
            {
                list.Label("IsoRimWeaponCards.Setting.BackDepth".Translate(Settings.backDepth.ToString("F2")), -1f);
                Settings.backDepth = Mathf.Round(list.Slider(Settings.backDepth, 0f, 1.50f) * 100f) / 100f;
            }
            list.Gap(4f);
            list.CheckboxLabeled("IsoRimWeaponCards.Setting.VanillaCarry".Translate(), ref Settings.replicateVanilla);
            list.Gap(4f);
            list.CheckboxLabeled("IsoRimWeaponCards.Setting.AutoDetect".Translate(), ref Settings.autoCollect,
                "IsoRimWeaponCards.Setting.AutoDetectTip".Translate());
            list.CheckboxLabeled("IsoRimWeaponCards.Setting.ScanFallback".Translate(), ref Settings.drawerScanFallback,
                "IsoRimWeaponCards.Setting.ScanFallbackTip".Translate());
            list.Gap(6f);
            DrawDrawerList(list);
        }

        private void DrawBuildingTab(Listing_Standard list)
        {
            list.CheckboxLabeled("IsoRimWeaponCards.Setting.Building".Translate(), ref Settings.buildingEnabled,
                "IsoRimWeaponCards.Setting.BuildingTip".Translate());
            list.Gap(4f);
            list.CheckboxLabeled("IsoRimWeaponCards.Setting.BuildingAutoDetect".Translate(), ref Settings.buildingAutoCollect,
                "IsoRimWeaponCards.Setting.BuildingAutoDetectTip".Translate());
            if (list.ButtonText("IsoRimWeaponCards.Setting.BuildingSweep".Translate()))
            {
                Messages.Message("IsoRimWeaponCards.Setting.BuildingSweepDone".Translate(),
                    MessageTypeDefOf.NeutralEvent, false);
            }
            List<string> seen = Settings.buildingSeen;
            if (seen == null || seen.Count == 0)
            {
                list.Gap(4f);
                list.Label("IsoRimWeaponCards.Setting.BuildingEmpty".Translate(), -1f);
                return;
            }
            list.Label("IsoRimWeaponCards.Setting.BuildingFound".Translate(), -1f,
                "IsoRimWeaponCards.Setting.BuildingFoundTip".Translate());
            float total = 0f;
            for (int i = 0; i < seen.Count; i++)
            {
                DrawerTune t = BuildingCards.Get(seen[i]);
                total += 24f + (t != null && t.expanded ? 126f : 0f);
            }
            float viewHeight = total + 6f;
            // Fill the rest of the page, same as the pawn tab.
            float remaining = Mathf.Max(120f, pageBodyHeight - list.CurHeight - 30f);
            Rect area = list.GetRect(Mathf.Min(viewHeight, remaining));
            DrawListScroll(area, viewHeight, ref buildingScroll, 1, delegate (float rowWidth, float offset)
            {
                float y = 0f;
                for (int i = 0; i < seen.Count; i++)
                {
                    DrawerTune t = BuildingCards.Get(seen[i]);
                    if (t == null)
                    {
                        continue;
                    }
                    Rect row = new Rect(0f, y + offset, rowWidth, 24f);
                    bool on = t.on;
                    Widgets.Checkbox(new Vector2(row.x, row.y + 2f), ref on, 18f);
                    if (on != t.on)
                    {
                        t.on = on;
                        BuildingCards.Save();
                    }
                    Rect keyRect = new Rect(row.x + 22f, row.y + 2f, Mathf.Max(40f, row.width - 22f - 96f), 22f);
                    Widgets.Label(keyRect, t.key);
                    TooltipHandler.TipRegion(keyRect, t.key);
                    if (Widgets.ButtonText(new Rect(row.width - 92f, row.y + 1f, 88f, 22f),
                        "IsoRimWeaponCards.Setting.Adjust".Translate()))
                    {
                        t.expanded = !t.expanded;
                        BuildingCards.Save();
                    }
                    y += 24f;
                    if (t.expanded)
                    {
                        // The rows are placed at "y + offset", so the tune has to start there too; starting at plain
                        // "y" put it above the visible box once the list had been scrolled and it looked like the
                        // sliders had vanished.
                        float ty = y + offset;
                        DrawTune(t, rowWidth, ref ty, true);
                        y = ty - offset;
                    }
                }
            });
        }

        // Third tab: the in-game entry and the floating window.
        private void DrawControlsTab(Listing_Standard list)
        {
            list.Label("IsoRimWeaponCards.Setting.ControlsIntro".Translate(), -1f);
            list.Gap(6f);
            list.CheckboxLabeled("IsoRimWeaponCards.Setting.EntryButton".Translate(), ref Settings.showEntryButton,
                "IsoRimWeaponCards.Setting.EntryButtonTip".Translate());
            list.Gap(6f);
            list.Label("IsoRimWeaponCards.Setting.DragHint".Translate(), -1f);
            list.Gap(6f);
            if (list.ButtonText("IsoRimWeaponCards.Setting.OpenFloat".Translate()))
            {
                Find.WindowStack.Add(new Window_IsoRimSettings());
            }
        }

        private void DrawDrawerList(Listing_Standard list)
        {
            List<string> seen = Settings.drawerSeen;
            if (seen == null || seen.Count == 0)
            {
                return;
            }
            list.Label("IsoRimWeaponCards.Setting.Found".Translate(), -1f,
                "IsoRimWeaponCards.Setting.FoundTip".Translate());
            if (list.ButtonText("IsoRimWeaponCards.Setting.Sweep".Translate()))
            {
                Drawers.Sweep(3);
                Messages.Message("IsoRimWeaponCards.Setting.SweepDone".Translate(),
                    MessageTypeDefOf.NeutralEvent, false);
            }
            float total = 0f;
            for (int i = 0; i < seen.Count; i++)
            {
                DrawerTune t = Drawers.Get(seen[i]);
                // row + (queue, plane X, plane Y, depth, reset button)
                total += 24f + (t != null && t.expanded ? 126f : 0f);
            }
            float viewHeight = total + 6f;
            // Fill the rest of the page: the list grows and shrinks with the window instead of a fixed 300px.
            float remaining = Mathf.Max(120f, pageBodyHeight - list.CurHeight - 30f);
            Rect area = list.GetRect(Mathf.Min(viewHeight, remaining));
            DrawListScroll(area, viewHeight, ref drawerScroll, 0, delegate (float rowWidth, float offset)
            {
                float y = 0f;
                for (int i = 0; i < seen.Count; i++)
                {
                    DrawerTune t = Drawers.Get(seen[i]);
                    if (t == null)
                    {
                        continue;
                    }
                    Rect row = new Rect(0f, y + offset, rowWidth, 24f);
                    bool on = t.on;
                    Widgets.Checkbox(new Vector2(row.x, row.y + 2f), ref on, 18f);
                    if (on != t.on)
                    {
                        t.on = on;
                        Drawers.Save();
                        Drawers.Rebuild();
                    }
                    string modName = Drawers.ModOf(t.key);
                    float nameWidth = 0f;
                    float keySpace = Mathf.Max(60f, row.width - 22f - 96f);
                    if (!string.IsNullOrEmpty(modName))
                    {
                        string shown = modName.Length > 22 ? modName.Substring(0, 21) + "…" : modName;
                        nameWidth = Mathf.Min(Text.CalcSize(shown).x, keySpace * 0.42f) + 8f;
                        Color old = GUI.color;
                        GUI.color = new Color(0.15f, 1f, 1f);
                        Widgets.Label(new Rect(row.x + 22f, row.y + 2f, nameWidth, 22f), shown);
                        GUI.color = old;
                    }
                    Rect keyRect = new Rect(row.x + 22f + nameWidth, row.y + 2f,
                        Mathf.Max(40f, keySpace - nameWidth), 22f);
                    Widgets.Label(keyRect, t.key);
                    TooltipHandler.TipRegion(keyRect, string.IsNullOrEmpty(modName)
                        ? t.key
                        : t.key + "\n" + "IsoRimWeaponCards.Setting.Source".Translate(modName).ToString());
                    if (Widgets.ButtonText(new Rect(row.width - 92f, row.y + 1f, 88f, 22f),
                        "IsoRimWeaponCards.Setting.Adjust".Translate()))
                    {
                        t.expanded = !t.expanded;
                        Drawers.Save();
                    }
                    y += 24f;
                    if (t.expanded)
                    {
                        // Rows sit at "y + offset", so the tune must start there as well; starting at plain "y" moved
                        // it out of the visible box as soon as the list had been scrolled.
                        float ty = y + offset;
                        DrawTune(t, rowWidth, ref ty);
                        y = ty - offset;
                    }
                }
            });
        }

        // A number you can click and type into. The value is committed when the field loses focus (clicking anywhere
        // else, tabbing away, or pressing enter). Focus is requested exactly once when editing starts - asking for it
        // every frame is what makes such a field keep grabbing focus and never let the click through.
        private void DrawEditableNumber(Rect rect, DrawerTune t, string field, float value, float min, float max, float roundTo, Action<float> set)
        {
            string id = t.key + "#" + field;
            if (editId != id)
            {
                Widgets.Label(rect, roundTo >= 1f ? Mathf.RoundToInt(value).ToString() : value.ToString("F2"));
                if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
                {
                    editId = id;
                    editText = roundTo >= 1f ? Mathf.RoundToInt(value).ToString() : value.ToString("F2");
                    editNeedsFocus = true;
                    Event.current.Use();
                }
                return;
            }

            // Clicking anywhere outside commits, even if that click lands on nothing focusable.
            if (Event.current.type == EventType.MouseDown && !Mouse.IsOver(rect))
            {
                CommitEdit(min, max, roundTo, set);
                return;
            }

            if (editNeedsFocus)
            {
                GUI.SetNextControlName(id);
                GUI.FocusControl(id);
                editNeedsFocus = false;
            }
            string next = GUI.TextField(rect, editText, 24);
            if (next != editText)
            {
                editText = next;
            }
            if (GUI.GetNameOfFocusedControl() != id)
            {
                CommitEdit(min, max, roundTo, set);
            }
        }

        private static void CommitEdit(float min, float max, float roundTo, Action<float> set)
        {
            float v;
            if (float.TryParse(editText, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                v = Mathf.Clamp(v, min, max);
                if (roundTo >= 1f)
                {
                    v = Mathf.RoundToInt(v);
                }
                set(v);
            }
            editId = null;
            editText = null;
            editNeedsFocus = false;
            GUI.FocusControl(null);
        }

        private void DrawTune(DrawerTune t, float width, ref float y, bool building = false)
        {
            Action save = building ? (Action)BuildingCards.Save : Drawers.Save;
            float x = 24f;
            float w = width - x - 8f;
            bool useQueue = t.useQueue;
            Widgets.CheckboxLabeled(new Rect(x, y, 132f, 22f), "IsoRimWeaponCards.Setting.UseQueue".Translate(), ref useQueue);
            if (useQueue != t.useQueue)
            {
                t.useQueue = useQueue;
                save();
            }
            int queue = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(x + 136f, y + 4f, w - 136f - 52f, 18f),
                t.queue, 2700f, 3200f, false, null, "2700", "3200", 1f));
            if (queue != t.queue)
            {
                t.queue = queue;
                save();
            }
            DrawEditableNumber(new Rect(width - 56f, y, 56f, 22f), t, "q", t.queue, 2700f, 3200f, 1f, delegate (float v) { t.queue = Mathf.RoundToInt(v); save(); });
            y += 24f;

            Widgets.Label(new Rect(x, y + 2f, 92f, 22f), "IsoRimWeaponCards.Setting.ShiftX".Translate());
            float sx = Widgets.HorizontalSlider(new Rect(x + 94f, y + 4f, w - 94f - 52f, 18f), t.shiftX,
                -1.5f, 1.5f, false, null, null, null, 0.01f);
            if (sx != t.shiftX)
            {
                t.shiftX = sx;
                save();
            }
            DrawEditableNumber(new Rect(width - 56f, y, 56f, 22f), t, "x", t.shiftX, -1.5f, 1.5f, 0.01f, delegate (float v) { t.shiftX = v; save(); });
            y += 24f;

            Widgets.Label(new Rect(x, y + 2f, 92f, 22f), "IsoRimWeaponCards.Setting.ShiftY".Translate());
            float sy = Widgets.HorizontalSlider(new Rect(x + 94f, y + 4f, w - 94f - 52f, 18f), t.shiftY,
                -1.5f, 1.5f, false, null, null, null, 0.01f);
            if (sy != t.shiftY)
            {
                t.shiftY = sy;
                save();
            }
            DrawEditableNumber(new Rect(width - 56f, y, 56f, 22f), t, "y", t.shiftY, -1.5f, 1.5f, 0.01f, delegate (float v) { t.shiftY = v; save(); });
            y += 24f;

            Widgets.Label(new Rect(x, y + 2f, 92f, 22f), "IsoRimWeaponCards.Setting.ShiftZ".Translate());
            float sz = Widgets.HorizontalSlider(new Rect(x + 94f, y + 4f, w - 94f - 52f, 18f), t.shiftZ,
                -1.5f, 1.5f, false, null, null, null, 0.01f);
            if (sz != t.shiftZ)
            {
                t.shiftZ = sz;
                save();
            }
            DrawEditableNumber(new Rect(width - 56f, y, 56f, 22f), t, "z", t.shiftZ, -1.5f, 1.5f, 0.01f, delegate (float v) { t.shiftZ = v; save(); });
            y += 26f;

            if (Widgets.ButtonText(new Rect(x, y, 120f, 22f), "IsoRimWeaponCards.Setting.Reset".Translate()))
            {
                if (building)
                {
                    BuildingCards.ResetToDefault(t);
                }
                else
                {
                    Drawers.ResetToDefault(t);
                }
                save();
            }
            y += 28f;
        }
    }

    [StaticConstructorOnStartup]
    public static class Patch
    {
        private const string Tag = "[IsoRimWeaponCards] ";
        [ThreadStatic] private static bool scope;
        [ThreadStatic] private static Matrix4x4 scopeMatrix;
        private static MethodInfo cardsTryGet;

        [ThreadStatic] private static bool backViewPush;
        private static readonly object[] TryArgs = new object[3];

        // Set while this mod draws something itself (the weapon, the hands). Those draws already carry the card
        // transform, so the global DrawMesh hook must leave them alone.
        [ThreadStatic] internal static bool SelfDraw;

        // What the renderer handed to DrawEquipmentAndApparelExtras for the pawn being drawn right now. The weapon
        // back mods replace that method, so capturing it here (before their prefix) is the only way to still know
        // the facing the game itself wanted to use.
        [ThreadStatic] private static Pawn capPawn;
        [ThreadStatic] private static Vector3 capDrawPos;
        [ThreadStatic] private static Rot4 capFacing;
        [ThreadStatic] private static bool capValid;

        private static bool lookedUpEqLoc;
        private static FieldInfo eqLocNorth;
        private static FieldInfo eqLocEast;
        private static FieldInfo eqLocSouth;
        private static FieldInfo eqLocWest;
        private static int helperPatched;
        [ThreadStatic] private static Pawn renderPawn;
        private static FieldInfo rendererPawnField;
        // Card cache refresh: IsoRim Paper2D Fix (isorim.uprightitems) shifts DrawHook.xform for a pawn sitting on a
        // chair in a RenderPawnAt prefix that runs AFTER IsoRim cached the card matrix, so the body got the shifted
        // matrix while parts folded by this mod got the stale one and drifted away. Re-record the card at the very
        // end of the prefix chain.
        private static FieldInfo drawHookActive;
        private static FieldInfo drawHookBillboard;
        private static MethodInfo cardOverlayUpright;
        // Seating: IsoRim Paper2D Fix's chair "depth" fix pushes a sitter forward by a view-dependent amount that gets
        // amplified on upper floors and jumps at a certain camera angle. The user asked to drop it entirely, so we
        // remember the card matrix from just after IsoRim set it and put it back before the card cache is refreshed.
        private static FieldInfo drawHookXform;
        private static MethodInfo posesSeatUnder;
        [ThreadStatic] private static Matrix4x4 preSeatXform;
        [ThreadStatic] private static bool preSeatValid;

        internal static bool ScopeActive
        {
            get { return scope; }
        }

        internal static Matrix4x4 ScopeMatrix
        {
            get { return scopeMatrix; }
        }

        static Patch()
        {
            try
            {
                cardsTryGet = AccessTools.TypeByName("IsoRim.Cards")?.GetMethod("TryGet", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                Harmony harmony = new Harmony("dogdie.isorimweaponcards");
                MethodInfo aiming = AccessTools.Method(typeof(PawnRenderUtility), "DrawEquipmentAiming", new[] { typeof(Thing), typeof(Vector3), typeof(float) });
                if (aiming != null)
                {
                    try
                    {
                        harmony.Patch(aiming, prefix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(AimingPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                    }
                    catch (Exception e)
                    {
                        Log.Warning(Tag + "aiming patch failed: " + e.Message);
                    }
                }
                // Capture only: runs before the weapon back prefixes and records what the renderer passed.
                MethodInfo extras = AccessTools.Method(typeof(PawnRenderUtility), "DrawEquipmentAndApparelExtras");
                if (extras != null)
                {
                    try
                    {
                        HarmonyMethod capture = new HarmonyMethod(typeof(Patch).GetMethod(nameof(CaptureExtras), BindingFlags.Static | BindingFlags.NonPublic));
                        capture.priority = Priority.First;
                        harmony.Patch(extras,
                            prefix: capture,
                            finalizer: new HarmonyMethod(typeof(Patch).GetMethod(nameof(ClearCapture), BindingFlags.Static | BindingFlags.NonPublic)));
                    }
                    catch (Exception e)
                    {
                        Log.Warning(Tag + "extras capture failed: " + e.Message);
                    }
                }
                try
                {
                    harmony.PatchAll(typeof(Patch).Assembly);
                }
                catch (Exception e)
                {
                    Log.Warning(Tag + "PatchAll failed: " + e.Message);
                }
                int hands = PatchShowMeYourHands(harmony);
                PatchWeaponBackHelper(harmony);
                // One entry point for every mesh draw of the game. Every Graphics.DrawMesh overload funnels into
                // Graphics.DrawMeshCore, so a single prefix sees each draw exactly once: no forwarded call can apply
                // the card transform twice (that doubling is what used to happen to extra parts).
                int draws = PatchDrawMeshCore(harmony);
                if (draws == 0)
                {
                    draws = PatchDrawMeshFallback(harmony);
                }
                Drawers.Install(harmony);
                BuildingCards.Install(harmony);
                PatchPlaySettingsEntry(harmony);
                PatchPawnDrawAt(harmony);
                PatchMoteDraw(harmony);
                PatchCardCacheRefresh(harmony);
                // The model pack used to be a separate mod (MyModelPack, exported by IsoRim's model binder); it lives
                // here now, so the binder's export folder is synced into this mod instead.
                MethodInfo shutdown = AccessTools.Method(typeof(Root), "Shutdown");
                if (shutdown != null)
                {
                    harmony.Patch(shutdown, postfix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(Shutdown_Postfix), BindingFlags.NonPublic | BindingFlags.Static)));
                }
                SyncModelPack("startup");
                Log.Message(Tag + "ready: aiming=" + (aiming != null) + ", capture=" + (extras != null) + ", cardsTryGet=" + (cardsTryGet != null)
                    + ", ShowMeYourHands=" + hands + ", helper=" + helperPatched + ", drawHook=" + draws
                    + ", drawers=" + Drawers.EnabledCount + ", scanFallback=" + Drawers.ScanFallbackNeeded + ".");
            }
            catch (Exception e)
            {
                Log.Error(Tag + "startup failed: " + e);
            }
        }

        // The game's own little button row (right edge of the screen on the map, next to "auto home area" and friends).
        // One icon there opens our floating settings window, so the mod can be tuned without digging through the
        // options dialog. The button can be switched off on the 控件设置 tab.
        private static void PatchPlaySettingsEntry(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(PlaySettings), "DoPlaySettingsGlobalControls");
                if (target == null)
                {
                    Log.Warning(Tag + "PlaySettings.DoPlaySettingsGlobalControls not found; no in-game entry button.");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(PlaySettings_Postfix), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "play settings hook failed: " + e.Message);
            }
        }

        private static void PlaySettings_Postfix(WidgetRow row, bool worldView)
        {
            try
            {
                WeaponCardSettings s = WeaponCardMod.Settings;
                if (s == null || !s.showEntryButton || row == null || worldView)
                {
                    return;
                }
                if (row.ButtonIcon(TexButton.SearchButton, "IsoRimWeaponCards.Entry.Tip".Translate()))
                {
                    Find.WindowStack.Add(new Window_IsoRimSettings());
                }
            }
            catch
            {
            }
        }

        // IsoRim can render a card pawn without going through PawnRenderer.RenderPawnAt, so the per-frame list used to
        // attach parts drawn in Pawn.DrawAt postfixes (an add-on's halo, for instance) could stay empty - the part then
        // found "no card pawn within 2.5 cells" and stayed flat on the ground. Pawn.DrawAt is always called for a pawn
        // that is being drawn, so note it here as well; ScanPawns already filters out anything that is not a card.
        private static void PatchPawnDrawAt(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(Pawn), "DrawAt");
            if (target == null)
            {
                Log.Warning(Tag + "Pawn.DrawAt not found; parts drawn from its postfixes may not attach to a card.");
                return;
            }
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(PawnDrawAt_Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "Pawn.DrawAt hook failed: " + e.Message);
            }
        }

        private static void PawnDrawAt_Prefix(Pawn __instance)
        {
            try
            {
                if (__instance == null || !__instance.Spawned)
                {
                    return;
                }
                drawAtPawn = __instance;
                List<Pawn> list = FramePawns;
                if (list.Count < 96 && !list.Contains(__instance))
                {
                    list.Add(__instance);
                }
            }
            catch
            {
            }
        }

        // The mote currently inside Mote.DrawMote, if any. Fold uses this to tell an attached mote (the fishing rod,
        // link1.Linked) from a free-standing one (a bullet, smoke, blood spray): only the attached kind belongs on
        // the card, the rest would be dragged onto it as a stretched smear when a pawn is on screen.
        [ThreadStatic] private static Mote currentMote;

        internal static Mote CurrentMote
        {
            get { return currentMote; }
        }

        private static void PatchMoteDraw(Harmony harmony)
        {
            try
            {
                MethodInfo drawMote = AccessTools.Method(typeof(Mote), "DrawMote");
                if (drawMote == null)
                {
                    return;
                }
                harmony.Patch(drawMote,
                    prefix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(MoteDraw_Prefix), BindingFlags.NonPublic | BindingFlags.Static)),
                    finalizer: new HarmonyMethod(typeof(Patch).GetMethod(nameof(MoteDraw_Finalizer), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "Mote.DrawMote hook failed: " + e.Message);
            }
        }

        private static void MoteDraw_Prefix(Mote __instance)
        {
            currentMote = __instance;
        }

        private static void MoteDraw_Finalizer()
        {
            currentMote = null;
        }

        // IsoRim Paper2D Fix (isorim.uprightitems) rewrites DrawHook.xform for a colonist sitting on a chair/bench, in
        // a RenderPawnAt prefix ordered Last + "after isorim.core" - i.e. after IsoRim has already stored the card
        // matrix in CardOverlay. The body is then drawn with the shifted matrix, but every part this mod folds reads
        // the stale cached one, so body and parts drift apart ("sprites fly away while sitting").
        // Refreshing the cache at the very end of the prefix chain keeps both on the same matrix.
        private static void PatchCardCacheRefresh(Harmony harmony)
        {
            try
            {
                cardOverlayUpright = AccessTools.TypeByName("IsoRim.CardOverlay")?.GetMethod("Upright", BindingFlags.Public | BindingFlags.Static);
                Type drawHook = AccessTools.TypeByName("IsoRim.DrawHook");
                drawHookActive = drawHook != null ? AccessTools.Field(drawHook, "active") : null;
                drawHookBillboard = drawHook != null ? AccessTools.Field(drawHook, "billboard") : null;
                drawHookXform = drawHook != null ? AccessTools.Field(drawHook, "xform") : null;
                posesSeatUnder = AccessTools.TypeByName("IsoRim.Poses")?.GetMethod("SeatUnder", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn) }, null);
                if (cardOverlayUpright == null || drawHookActive == null)
                {
                    Log.Warning(Tag + "IsoRim.CardOverlay.Upright not found; sitting pawns may drift when another mod shifts the card.");
                    return;
                }
                MethodInfo renderPawn = AccessTools.Method(typeof(PawnRenderer), "RenderPawnAt");
                if (renderPawn == null)
                {
                    return;
                }
                // Record the matrix right after IsoRim has set it (and before IsoRim Paper2D Fix rewrites it), so a
                // sitter can be put back on the un-shifted matrix when that mod's seat-depth push is to be ignored.
                if (drawHookXform != null && posesSeatUnder != null)
                {
                    harmony.Patch(renderPawn,
                        prefix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(RecordSeatXform), BindingFlags.NonPublic | BindingFlags.Static))
                        {
                            priority = Priority.LowerThanNormal,
                            after = new[] { "isorim.core" }
                        });
                }
                harmony.Patch(renderPawn,
                    prefix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(RefreshCardCache), BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        priority = Priority.Last,
                        after = new[] { "isorim.uprightitems.workshop" }
                    });
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "card cache refresh hook failed: " + e.Message);
            }
        }

        // Runs right after IsoRim set DrawHook.xform and before IsoRim Paper2D Fix's seat-depth rewrite. Only sitters
        // are remembered; other pawns keep whatever that mod does (trap/bed hosts are not touched by this).
        private static void RecordSeatXform(PawnRenderer __instance)
        {
            preSeatValid = false;
            try
            {
                if (posesSeatUnder == null || drawHookXform == null || drawHookActive == null || __instance == null)
                {
                    return;
                }
                if (!(bool)drawHookActive.GetValue(null))
                {
                    return;
                }
                if (rendererPawnField == null)
                {
                    rendererPawnField = AccessTools.Field(typeof(PawnRenderer), "pawn");
                }
                Pawn pawn = rendererPawnField != null ? rendererPawnField.GetValue(__instance) as Pawn : null;
                if (pawn == null || pawn.Dead || !pawn.Spawned)
                {
                    return;
                }
                if (posesSeatUnder.Invoke(null, new object[] { pawn }) == null)
                {
                    return;   // not sitting
                }
                preSeatXform = (Matrix4x4)drawHookXform.GetValue(null);
                preSeatValid = true;
            }
            catch
            {
            }
        }

        private static void RefreshCardCache(PawnRenderer __instance)
        {
            try
            {
                if (__instance == null || cardOverlayUpright == null || drawHookActive == null)
                {
                    return;
                }
                // Only while this pawn is being drawn as a card; otherwise the cache should stay empty.
                if (!(bool)drawHookActive.GetValue(null))
                {
                    return;
                }
                if (drawHookBillboard != null && (bool)drawHookBillboard.GetValue(null))
                {
                    return;
                }
                if (rendererPawnField == null)
                {
                    rendererPawnField = AccessTools.Field(typeof(PawnRenderer), "pawn");
                }
                Pawn pawn = rendererPawnField != null ? rendererPawnField.GetValue(__instance) as Pawn : null;
                if (pawn == null)
                {
                    return;
                }
                // Drop the seat-depth push for a sitter: put the un-shifted matrix back before re-recording the card,
                // so body and parts both stay where IsoRim itself would put them.
                if (preSeatValid && drawHookXform != null)
                {
                    drawHookXform.SetValue(null, preSeatXform);
                }
                preSeatValid = false;
                cardOverlayUpright.Invoke(null, new object[] { pawn });
            }
            catch
            {
            }
        }

        internal const string ModelPackName = "MyModelPack";

        private static void Shutdown_Postfix()
        {
            SyncModelPack("shutdown");
        }

        // IsoRim's model binder exports a whole mod folder (Defs, Patches, Models) into the save-data folder. That pack
        // used to be copied into Mods\MyModelPack as its own mod; now it is copied into this mod's own folders, so it
        // loads as part of IsoRim Weapon Cards and the extra mod is not needed. New model bindings therefore take
        // effect on the next start, exactly as before.
        internal static void SyncModelPack(string when)
        {
            try
            {
                string source = Path.Combine(Path.Combine(GenFilePaths.SaveDataFolderPath, "IsoRimBinder"), ModelPackName);
                if (!System.IO.Directory.Exists(source))
                {
                    return;
                }
                string target = null;
                foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
                {
                    if (mod != null && mod.PackageId == "dogdie.isorimweaponcards")
                    {
                        target = mod.RootDir;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(target))
                {
                    return;
                }
                int copied = 0;
                foreach (string sub in new[] { "Defs", "Patches", "Models" })
                {
                    string from = Path.Combine(source, sub);
                    if (System.IO.Directory.Exists(from))
                    {
                        copied += CopyTree(from, Path.Combine(target, sub));
                    }
                }
                Log.Message(Tag + "model pack synced into " + target + " (" + when + ", " + copied + " files).");
            }
            catch (Exception e)
            {
                Log.WarningOnce(Tag + "model pack sync failed: " + e.Message, 0x524F97);
            }
        }

        private static int CopyTree(string source, string target)
        {
            int copied = 0;
            System.IO.Directory.CreateDirectory(target);
            foreach (string file in System.IO.Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                copied++;
            }
            foreach (string dir in System.IO.Directory.GetDirectories(source))
            {
                copied += CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
            return copied;
        }

        private static void CaptureExtras(Pawn pawn, Vector3 drawPos, Rot4 facing)
        {
            try
            {
                capPawn = pawn;
                capDrawPos = drawPos;
                capFacing = facing;
                capValid = pawn != null;
            }
            catch
            {
            }
        }

        private static void ClearCapture()
        {
            capValid = false;
            capPawn = null;
        }

        // First, not Last: other mods put their own prefixes on this method and draw the weapon themselves, and a
        // prefix that returns false stops the ones after it. Running first means a card pawn is drawn here and theirs
        // never runs; anything that is not a card is passed straight on to them.
        [HarmonyPriority(Priority.First)]
        private static bool AimingPrefix(Thing eq, ref Vector3 drawLoc, ref float aimAngle)
        {
            if (WeaponCardMod.Settings == null || !WeaponCardMod.Settings.enabled || eq == null)
            {
                return true;
            }
            try
            {
                Pawn pawn = PawnOf(eq);
                if (pawn == null || !Api.IsCard(pawn))
                {
                    return true;
                }
                // Decided for every draw (aiming too, where the angle is not one of the carried pair): if the card shows
                // this pawn's back, Draw may push the weapon behind the card plane.
                backViewPush = !WeaponCardMod.Settings.hideFromBehind && SeenFromBehind(pawn);
                // Hiding the weapon when the card shows the pawn's back is a taste choice, not a requirement: with the
                // vanilla carried placement below, the weapon is put behind the body (EqLocNorth) and the card matrix
                // can let the body cover it. The setting lets both looks be compared in game.
                if (WeaponCardMod.Settings.hideFromBehind && SeenFromBehind(pawn))
                {
                    return false;
                }
                // Idle (carried) draw coming from a weapon back mod: replace its fixed angle with the game's own
                // carried placement, driven by the facing the renderer passed. Both values go back through ref, so
                // the postfixes that record the weapon pose (and therefore the hand sprites) see the same numbers.
                // Other mods add a degree or two of sway/recoil to the carried angle (mostly while walking), so an
                // exact 143/217 match missed almost every moving frame. Match with a tolerance; the exact vanilla
                // value is written back below either way.
                bool carried = (aimAngle > 138f && aimAngle < 148f) || (aimAngle > 212f && aimAngle < 222f);
                if (carried && WeaponCardMod.Settings.replicateVanilla && capValid && capPawn == pawn && CallerIsWeaponBack())
                {
                    float factor = pawn.ageTracker != null ? pawn.ageTracker.CurLifeStage.equipmentDrawDistanceFactor : 1f;
                    Vector3 offset = EqLoc(capFacing) * factor;
                    // The game's carried offsets always nudge the weapon TOWARDS the viewer (they were tuned to keep
                    // a flat, top-down weapon clear of the pawn sprite), so on a card the weapon sits in front of the
                    // body and looks painted on. When the card shows the pawn's back and the player asked for the
                    // body to hide it instead of plain hiding, mirror that depth part to the far side and push it a
                    // little further, so the card's body covers it at every camera angle - not only when the fixed
                    // world offset happens to point away from the camera.
                    // Card shows the pawn's back: instead of shifting the weapon in world space (which turns into a
                    // different offset every camera angle), ask Draw to push it behind the card plane in CARD space.
                    backViewPush = !WeaponCardMod.Settings.hideFromBehind && capFacing == Rot4.North;
                    drawLoc = capDrawPos + offset;
                    // Keep whatever sway the other mods add on top of the carried angle (mostly visible while walking)
                    // and move only its base: the direction comes from the card facing, the wobble stays theirs.
                    float baseAngle = Mathf.Abs(aimAngle - 143f) < Mathf.Abs(aimAngle - 217f) ? 143f : 217f;
                    float sway = aimAngle - baseAngle;
                    aimAngle = (capFacing == Rot4.West ? 217f : 143f) + sway;
                }
                float num = aimAngle - 90f;
                Mesh mesh;
                if (aimAngle > 20f && aimAngle < 160f)
                {
                    mesh = MeshPool.plane10;
                    num += eq.def.equippedAngleOffset;
                }
                else if (aimAngle > 200f && aimAngle < 340f)
                {
                    mesh = MeshPool.plane10Flip;
                    num -= 180f;
                    num -= eq.def.equippedAngleOffset;
                }
                else
                {
                    mesh = MeshPool.plane10;
                    num += eq.def.equippedAngleOffset;
                }
                num %= 360f;
                CompEquippable comp = eq.TryGetComp<CompEquippable>();
                if (comp != null)
                {
                    EquipmentUtility.Recoil(eq.def, EquipmentUtility.GetRecoilVerb(comp.AllVerbs), out Vector3 drawOffset, out float angleOffset, aimAngle);
                    drawLoc += drawOffset;
                    num += angleOffset;
                }
                Material material = MatOf(eq);
                Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(num, Vector3.up), new Vector3(eq.Graphic.drawSize.x, 0f, eq.Graphic.drawSize.y));
                Draw(pawn, mesh, matrix, material);
                return false;
            }
            catch (Exception e)
            {
                Log.WarningOnce(Tag + "aiming draw failed: " + e.Message, 0x524F81);
                return true;
            }
        }

        // The game's own per-facing carried offsets (PawnRenderUtility.EqLocNorth/East/South/West).
        private static Vector3 EqLoc(Rot4 facing)
        {
            try
            {
                if (!lookedUpEqLoc)
                {
                    lookedUpEqLoc = true;
                    eqLocNorth = AccessTools.Field(typeof(PawnRenderUtility), "EqLocNorth");
                    eqLocEast = AccessTools.Field(typeof(PawnRenderUtility), "EqLocEast");
                    eqLocSouth = AccessTools.Field(typeof(PawnRenderUtility), "EqLocSouth");
                    eqLocWest = AccessTools.Field(typeof(PawnRenderUtility), "EqLocWest");
                }
                FieldInfo field = facing.AsInt == 0 ? eqLocNorth : facing.AsInt == 1 ? eqLocEast : facing.AsInt == 2 ? eqLocSouth : eqLocWest;
                return field != null ? (Vector3)field.GetValue(null) : Vector3.zero;
            }
            catch
            {
                return Vector3.zero;
            }
        }

        private static int PatchShowMeYourHands(Harmony harmony)
        {
            try
            {
                Type drawer = AccessTools.TypeByName("ShowMeYourHands.HandDrawer");
                if (drawer == null)
                {
                    return 0;
                }
                int patched = 0;
                foreach (string name in new[] { "drawMainHand", "drawOffHand" })
                {
                    MethodInfo m = drawer.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (m == null)
                    {
                        continue;
                    }
                    harmony.Patch(m,
                        prefix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(HandPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                        finalizer: new HarmonyMethod(typeof(Patch).GetMethod(nameof(HandFinalizer), BindingFlags.Static | BindingFlags.NonPublic)));
                    patched++;
                }
                return patched;
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "Show Me Your Hands patch failed: " + e.Message);
                return 0;
            }
        }

        private static bool HandPrefix(object __instance, bool flipped, out bool __state)
        {
            __state = false;
            try
            {
                if (WeaponCardMod.Settings == null || !WeaponCardMod.Settings.enabled)
                {
                    return true;
                }
                ThingComp comp = __instance as ThingComp;
                Pawn pawn = comp != null ? comp.parent as Pawn : null;
                if (pawn == null || !Api.IsCard(pawn) || cardsTryGet == null)
                {
                    return true;
                }
                if (SeenFromBehind(pawn))
                {
                    return false;
                }
                TryArgs[0] = pawn;
                TryArgs[1] = null;
                TryArgs[2] = null;
                if (!(bool)cardsTryGet.Invoke(null, TryArgs))
                {
                    return true;
                }
                Matrix4x4 xform = (Matrix4x4)TryArgs[1];
                Matrix4x4 extra = (Matrix4x4)TryArgs[2];
                scopeMatrix = extra * xform * Matrix4x4.Translate(CardUpLift(xform, WeaponCardMod.Settings.handHeight));
                scope = true;
                __state = true;
                return true;
            }
            catch (Exception e)
            {
                Log.WarningOnce(Tag + "hand scope failed: " + e.Message, 0x524F91);
                return true;
            }
        }

        private static Exception HandFinalizer(bool __state)
        {
            if (__state)
            {
                scope = false;
            }
            return null;
        }

        private static Material MatOf(Thing eq)
        {
            return eq.Graphic is Graphic_StackCount stack
                ? stack.SubGraphicForStackCount(1, eq.def).MatSingleFor(eq)
                : eq.Graphic.MatSingleFor(eq);
        }

        // Moves a world position so that, once the card transform is applied, the point rises by the same on-screen
        // amount a world-Y lift of h used to give - but straight up the card, with no sideways or depth leak.
        private static Vector3 CardUpLift(Matrix4x4 xform, float h)
        {
            try
            {
                Vector3 pre = xform.inverse.MultiplyVector(Vector3.up);
                float scale = xform.MultiplyVector(Vector3.up).magnitude;
                if (pre.sqrMagnitude > 1e-12f && scale > 1e-6f)
                {
                    return pre * (h * scale);
                }
            }
            catch
            {
            }
            return new Vector3(0f, h, 0f);
        }

        // Screen-space placement for the plane sliders: project the point with IsoRim's own camera, move it by the
        // requested number of cells in pixels, and turn it back into a world point at the same height. Doing it on
        // screen keeps the part's apparent distance to the card constant at every camera angle, which a world-space
        // lift can never do (lifting in the world always moves the part closer to or further from the camera).
        // Returns false when IsoRim's camera is not ready, so the caller can fall back to the world-space shift.
        internal static bool ScreenShift(Vector3 at, float right, float up, float depth, out Vector3 shift)
        {
            shift = Vector3.zero;
            if (Mathf.Abs(right) < 1e-4f && Mathf.Abs(up) < 1e-4f && Mathf.Abs(depth) < 1e-4f)
            {
                return true;
            }
            try
            {
                if (!View3D.Ready)
                {
                    return false;
                }
                Vector2 px = View3D.WorldToScreen(at);
                // How many pixels one cell spans at this spot, so "cells" means the same as elsewhere in the mod.
                Vector2 unit = View3D.WorldToScreen(at + View3D.GroundForward) - px;
                float perCell = unit.magnitude;
                if (perCell < 1e-3f)
                {
                    return false;
                }
                Vector2 target = px + new Vector2(right, up) * perCell;
                Vector3 moved = View3D.ScreenToHeight(target, at.y);
                shift = moved - at;
                if (Mathf.Abs(depth) >= 1e-4f)
                {
                    // Depth is meant to move the part towards/away from the viewer, so it stays a world translation.
                    Vector3 camRight;
                    Vector3 camUp;
                    Vector3 camForward;
                    if (ScreenAxes(out camRight, out camUp, out camForward))
                    {
                        shift -= Unit(camForward) * depth;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // A shift of (right, depth) for a card, both applied after the card transform:
        //   right -> View3D.Right, IsoRim's own camera-right vector. This is the one confirmed correct in game.
        //            (Find.Camera.transform is NOT usable here: under Waylith it does not carry the real render
        //            camera, so its "right" was a world axis and the slider stopped following the screen.)
        //   depth -> -View3D.Forward (towards / away from the viewer).
        // The vertical is not here: it goes in before the transform as a world Z offset (see Drawers.Fold).
        internal static Vector3 CardShift(float right, float depth)
        {
            if (Mathf.Abs(right) < 1e-4f && Mathf.Abs(depth) < 1e-4f)
            {
                return Vector3.zero;
            }
            Vector3 camRight = View3D.Right;
            Vector3 camForward = View3D.Forward;
            if (camRight.sqrMagnitude < 1e-6f || camForward.sqrMagnitude < 1e-6f)
            {
                Vector3 fallbackRight;
                Vector3 fallbackUp;
                Vector3 fallbackForward;
                if (ScreenAxes(out fallbackRight, out fallbackUp, out fallbackForward))
                {
                    if (camRight.sqrMagnitude < 1e-6f)
                    {
                        camRight = fallbackRight;
                    }
                    if (camForward.sqrMagnitude < 1e-6f)
                    {
                        camForward = fallbackForward;
                    }
                }
            }
            return Unit(camRight) * right - Unit(camForward) * depth;
        }

        private static Vector3 Unit(Vector3 v)
        {
            float m = v.magnitude;
            return m > 1e-5f ? v / m : Vector3.zero;
        }

        // The basis of the camera that actually draws the scene.
        private static bool ScreenAxes(out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            right = Vector3.zero;
            up = Vector3.zero;
            forward = Vector3.zero;
            try
            {
                Camera cam = Camera.current;
                if (cam == null)
                {
                    cam = Find.Camera;
                }
                if (cam == null)
                {
                    return false;
                }
                Transform t = cam.transform;
                right = t.right;
                up = t.up;
                forward = t.forward;
                return right.sqrMagnitude > 1e-6f && up.sqrMagnitude > 1e-6f && forward.sqrMagnitude > 1e-6f;
            }
            catch
            {
                return false;
            }
        }

        // Logged once, so the three directions can be checked against what the screen shows.
        private static bool loggedBasis;

        internal static void LogBasisOnce()
        {
            if (loggedBasis)
            {
                return;
            }
            try
            {
                if (!View3D.Ready)
                {
                    return;
                }
                loggedBasis = true;
                Vector3 r;
                Vector3 u;
                Vector3 f;
                bool camOk = ScreenAxes(out r, out u, out f);
                Log.Message(Tag + "camera basis: camera=" + (camOk
                        ? ("right=" + r.ToString("F3") + " up=" + u.ToString("F3") + " forward=" + f.ToString("F3"))
                        : "unavailable")
                    + " | View3D: yaw=" + View3D.Yaw.ToString("F1") + " pitch=" + View3D.Pitch.ToString("F1")
                    + " right=" + View3D.Right.ToString("F3") + " up=" + View3D.Up.ToString("F3")
                    + " forward=" + View3D.Forward.ToString("F3")
                    + " (plane right uses the camera right, plane up uses world +Y, depth uses -camera forward).");
            }
            catch
            {
            }
        }

        // The weapon's height is applied along the card's own "up" rather than along world Y. A world-Y lift is turned
        // by the card matrix's pitch and then yaw, so part of it leaked sideways and into the camera depth (it looked
        // like the weapon drifted to the upper left, or to the pawn's right front, as the camera turned).
        private static void Draw(Pawn pawn, Mesh mesh, Matrix4x4 matrix, Material material)
        {
            Vector4 column = matrix.GetColumn(3);
            Vector3 position = new Vector3(column.x, column.y, column.z);
            Matrix4x4 target;
            if (cardsTryGet == null)
            {
                target = Matrix4x4.TRS(position, matrix.rotation, matrix.lossyScale);
                SelfDraw = true;
                try
                {
                    Graphics.DrawMesh(mesh, target, material, 0);
                }
                finally
                {
                    SelfDraw = false;
                }
                return;
            }
            TryArgs[0] = pawn;
            TryArgs[1] = null;
            TryArgs[2] = null;
            if (!(bool)cardsTryGet.Invoke(null, TryArgs))
            {
                SelfDraw = true;
                try
                {
                    Graphics.DrawMesh(mesh, matrix, material, 0);
                }
                finally
                {
                    SelfDraw = false;
                }
                return;
            }
            Matrix4x4 xform = (Matrix4x4)TryArgs[1];
            Matrix4x4 extra = (Matrix4x4)TryArgs[2];
            Vector3 lifted = position + CardUpLift(xform, WeaponCardMod.Settings.handHeight);
            if (backViewPush)
            {
                // Pushes the weapon away from the camera so the card's body covers it. The direction has to be the
                // camera's line of sight, not a world axis: a world axis pointed sideways as soon as the camera turned.
                lifted += CameraAway(WeaponCardMod.Settings.backDepth);
            }
            target = Matrix4x4.TRS(lifted, matrix.rotation, matrix.lossyScale);
            SelfDraw = true;
            try
            {
                Graphics.DrawMesh(mesh, extra * xform * target, material, 0);
            }
            finally
            {
                SelfDraw = false;
            }
        }

        // The camera's line of sight (straight away from the viewer) times depth, in world cells.
        internal static Vector3 CameraAway(float depth)
        {
            try
            {
                float pitch = View3D.Pitch * Mathf.Deg2Rad;
                float yaw = View3D.Yaw * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), -Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
                if (dir.sqrMagnitude > 1e-6f)
                {
                    return dir.normalized * depth;
                }
            }
            catch
            {
            }
            return Vector3.zero;
        }

        // The weapon-back library draws with a helper of its own (SYS.DrawEquipment_WeaponBackPatch.DrawEquipmentAiming)
        // for weapons that carry its extension comp; those never reach the game's DrawEquipmentAiming, so they are
        // taken here and placed on the card the same way.
        private static bool SysHelperPrefix(Thing eq, ref Vector3 drawLoc, ref float aimAngle)
        {
            if (WeaponCardMod.Settings == null || !WeaponCardMod.Settings.enabled || eq == null)
            {
                return true;
            }
            try
            {
                Pawn pawn = PawnOf(eq);
                if (pawn == null || !Api.IsCard(pawn))
                {
                    return true;
                }
                backViewPush = !WeaponCardMod.Settings.hideFromBehind && SeenFromBehind(pawn);
                if (WeaponCardMod.Settings.hideFromBehind && SeenFromBehind(pawn))
                {
                    return false;
                }
                if (WeaponCardMod.Settings.replicateVanilla && capValid && capPawn == pawn)
                {
                    float factor = pawn.ageTracker != null ? pawn.ageTracker.CurLifeStage.equipmentDrawDistanceFactor : 1f;
                    drawLoc = capDrawPos + EqLoc(capFacing) * factor;
                    aimAngle = capFacing == Rot4.West ? 217f : 143f;
                }
                float num = aimAngle - 90f;
                Mesh mesh;
                if (aimAngle > 20f && aimAngle < 160f)
                {
                    mesh = MeshPool.plane10;
                    num += eq.def.equippedAngleOffset;
                }
                else if (aimAngle > 200f && aimAngle < 340f)
                {
                    mesh = MeshPool.plane10Flip;
                    num -= 180f;
                    num -= eq.def.equippedAngleOffset;
                }
                else
                {
                    mesh = MeshPool.plane10;
                    num += eq.def.equippedAngleOffset;
                }
                num %= 360f;
                Material material = MatOf(eq);
                Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(num, Vector3.up), new Vector3(eq.Graphic.drawSize.x, 0f, eq.Graphic.drawSize.y));
                Draw(pawn, mesh, matrix, material);
                return false;
            }
            catch (Exception e)
            {
                Log.WarningOnce(Tag + "weapon back helper draw failed: " + e.Message, 0x524F93);
                return true;
            }
        }

        private static void PatchWeaponBackHelper(Harmony harmony)
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = null;
                    try
                    {
                        type = assembly.GetType("SYS.DrawEquipment_WeaponBackPatch", false);
                    }
                    catch
                    {
                    }
                    if (type == null)
                    {
                        continue;
                    }
                    MethodInfo helper = type.GetMethod("DrawEquipmentAiming", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
                        new[] { typeof(Thing), typeof(Vector3), typeof(float), typeof(bool) }, null);
                    if (helper == null)
                    {
                        continue;
                    }
                    try
                    {
                        harmony.Patch(helper, prefix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(SysHelperPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                        helperPatched++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning(Tag + "weapon back helper patch failed: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "weapon back helper setup failed: " + e.Message);
            }
        }

        #region draw hook

        // Guards against a draw being folded in twice: a public DrawMesh overload forwards to another one, and the
        // inner one would see the matrix the outer one already transformed. While a patched overload is on the stack
        // the inner ones stay out.
        [ThreadStatic] private static int drawChain;

        // Every Graphics.DrawMesh overload of Waylith funnels into Graphics.DrawMeshCore(mesh, matrix, material,
        // layer, submeshIndex, properties, renderQueue). One prefix here therefore sees each draw exactly once, which
        // is what removes the doubled transform: a 4-argument call that forwards to a 7-argument one used to reach the
        // old per-overload prefixes twice.
        private static int PatchDrawMeshCore(Harmony harmony)
        {
            try
            {
                MethodInfo core = AccessTools.Method(typeof(Graphics), "DrawMeshCore", new[]
                {
                    typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(int), typeof(int), typeof(MaterialPropertyBlock), typeof(int)
                });
                if (core == null)
                {
                    Log.Warning(Tag + "Graphics.DrawMeshCore not found; falling back to the public DrawMesh overloads (no render queue control).");
                    return 0;
                }
                // Priority.First: IsoRim's own Floors.PatchDrawing puts Single_Prefix on this same DrawMeshCore to map
                // a matrix from game space onto its stacked floor (z -= shift, y += height/squash). Our card transform
                // is built around game-space pivots, so it has to run BEFORE that mapping; running after it produced a
                // card matrix applied to an already-mapped matrix, and the part vanished into the floor's row band.
                harmony.Patch(core,
                    prefix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(DrawMeshCorePrefix), BindingFlags.Static | BindingFlags.NonPublic))
                    {
                        priority = Priority.First
                    });
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "draw hook failed: " + e.Message);
                return 0;
            }
        }

        // Used when Graphics.DrawMeshCore cannot be hooked. The public overloads are enough to fold the draws in, but
        // the render queue of a part cannot be chosen this way.
        private static int PatchDrawMeshFallback(Harmony harmony)
        {
            int patched = 0;
            MethodInfo enter = typeof(Patch).GetMethod(nameof(EntryPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo leave = typeof(Patch).GetMethod(nameof(EntryFinalizer), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo enterPos = typeof(Patch).GetMethod(nameof(EntryPositionPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo leavePos = typeof(Patch).GetMethod(nameof(EntryPositionFinalizer), BindingFlags.Static | BindingFlags.NonPublic);
            patched += PatchOne(harmony, AccessTools.Method(typeof(Graphics), "DrawMesh", new[] { typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(int) }), enter, leave);
            patched += PatchOne(harmony, AccessTools.Method(typeof(Graphics), "DrawMesh", new[] { typeof(Mesh), typeof(Vector3), typeof(Quaternion), typeof(Material), typeof(int) }), enterPos, leavePos);
            Log.Warning(Tag + "draw hook fallback: " + patched + " overloads patched (render queue control unavailable).");
            return patched;
        }

        private static int PatchOne(Harmony harmony, MethodInfo target, MethodInfo prefix, MethodInfo finalizer)
        {
            if (target == null || prefix == null)
            {
                return 0;
            }
            try
            {
                harmony.Patch(target,
                    prefix: new HarmonyMethod(prefix),
                    finalizer: finalizer != null ? new HarmonyMethod(finalizer) : null);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "draw hook failed on " + target.Name + ": " + e.Message);
                return 0;
            }
        }

        private static void DrawMeshCorePrefix(ref Matrix4x4 matrix, Material material, int layer, ref int renderQueue)
        {
            try
            {
                if (SelfDraw || drawChain > 0)
                {
                    return;
                }
                ApplyDrawHook(ref matrix, material, layer, ref renderQueue);
            }
            catch (Exception e)
            {
                Log.WarningOnce(Tag + "draw hook failed: " + e.Message, 0x524FA1);
            }
        }

        // Fallback entry for Graphics.DrawMesh(mesh, matrix, material, layer).
        private static void EntryPrefix(ref Matrix4x4 matrix, Material material, int layer, out bool __state)
        {
            __state = false;
            if (SelfDraw || drawChain > 0)
            {
                return;
            }
            int queue = 0;
            try
            {
                ApplyDrawHook(ref matrix, material, layer, ref queue);
            }
            catch (Exception e)
            {
                Log.WarningOnce(Tag + "draw hook failed: " + e.Message, 0x524FA2);
            }
            drawChain++;
            __state = true;
        }

        private static void EntryFinalizer(bool __state)
        {
            if (__state && drawChain > 0)
            {
                drawChain--;
            }
        }

        // Fallback entry for Graphics.DrawMesh(mesh, position, rotation, material, layer). The card transform scales
        // the mesh non-uniformly, which position/rotation cannot express, so the draw is redone with a matrix.
        private static bool EntryPositionPrefix(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer)
        {
            if (SelfDraw || drawChain > 0)
            {
                return true;
            }
            int queue = 0;
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
            bool changed;
            try
            {
                changed = ApplyDrawHook(ref matrix, material, layer, ref queue);
            }
            catch (Exception e)
            {
                Log.WarningOnce(Tag + "draw hook failed: " + e.Message, 0x524FA3);
                return true;
            }
            if (!changed)
            {
                return true;
            }
            SelfDraw = true;
            try
            {
                Graphics.DrawMesh(mesh, matrix, material, layer);
            }
            finally
            {
                SelfDraw = false;
            }
            return false;
        }

        private static void EntryPositionFinalizer()
        {
        }

        // Hand scope first (that draw is this mod's own), then a card-form building's contents, then the pawn card paths.
        private static bool ApplyDrawHook(ref Matrix4x4 matrix, Material material, int layer, ref int renderQueue)
        {
            if (scope)
            {
                matrix = scopeMatrix * matrix;
                return true;
            }
            if (BuildingCards.Active)
            {
                // The building scope owns every draw made while a card building draws: it either folds the draw in
                // (method switched on) or leaves it alone. Returning here keeps the pawn-card paths out of it, so a
                // piece can never be transformed twice.
                BuildingCards.TryApply(ref matrix, ref renderQueue);
                return true;
            }
            if (WeaponCardMod.Settings == null || !WeaponCardMod.Settings.enabled)
            {
                return false;
            }
            if (Drawers.Apply(ref matrix, ref renderQueue))
            {
                return true;
            }
            LogBasisOnce();
            Drawers.MaybeDetect(matrix, material, layer);
            return false;
        }

        #endregion

        #region card state

        // The card matrices of one pawn are asked for on every draw of its parts; keep the answers for the frame.
        private struct CardEntry
        {
            public Pawn pawn;
            public Matrix4x4 xform;
            public Matrix4x4 extra;
            public bool ok;
        }

        [ThreadStatic] private static List<CardEntry> cardCache;
        [ThreadStatic] private static int cardCacheFrame = -1;

        // Every pawn that was drawn as a card this frame, plus the ones of the frame before. Parts some mods draw
        // outside PawnRenderer.RenderPawnAt (motes and the like) are matched to the nearest of these, and a mote can
        // be drawn before its pawn in the same frame.
        [ThreadStatic] private static List<Pawn> framePawns;
        [ThreadStatic] private static List<Pawn> prevFramePawns;
        [ThreadStatic] private static int framePawnsStamp = -1;

        private static List<Pawn> FramePawns
        {
            get
            {
                int frame = Time.frameCount;
                if (framePawns == null)
                {
                    framePawns = new List<Pawn>(48);
                    prevFramePawns = new List<Pawn>(48);
                    framePawnsStamp = frame;
                }
                else if (framePawnsStamp != frame)
                {
                    List<Pawn> swap = prevFramePawns;
                    prevFramePawns = framePawns;
                    framePawns = swap;
                    framePawns.Clear();
                    framePawnsStamp = frame;
                }
                return framePawns;
            }
        }

        private static List<CardEntry> CardCache
        {
            get
            {
                int frame = Time.frameCount;
                if (cardCache == null)
                {
                    cardCache = new List<CardEntry>(64);
                    cardCacheFrame = frame;
                }
                else if (cardCacheFrame != frame)
                {
                    cardCache.Clear();
                    cardCacheFrame = frame;
                }
                return cardCache;
            }
        }

        // The pawn whose card is being drawn right now: the renderer's pawn, or the one the equipment draw was
        // called for (the weapon back mods replace that method, so both are kept).
        internal static Pawn CurrentCardPawn()
        {
            if (capValid && capPawn != null)
            {
                return capPawn;
            }
            return renderPawn;
        }

        // The pawn currently inside Pawn.DrawAt. Parts drawn from its postfixes (an add-on's halo) belong to it even
        // when the frame list has no entry for it - on IsoRimZ's upper floors the pawn is not drawn through
        // RenderPawnAt, which is exactly what left those halos flat.
        [ThreadStatic] private static Pawn drawAtPawn;

        internal static Pawn DrawAtPawn
        {
            get { return drawAtPawn; }
        }

        internal static bool CardMatricesFor(Pawn pawn, out Matrix4x4 xform, out Matrix4x4 extra)
        {
            xform = Matrix4x4.identity;
            extra = Matrix4x4.identity;
            if (pawn == null || cardsTryGet == null)
            {
                return false;
            }
            List<CardEntry> cache = CardCache;
            for (int i = 0; i < cache.Count; i++)
            {
                if (cache[i].pawn == pawn)
                {
                    xform = cache[i].xform;
                    extra = cache[i].extra;
                    return cache[i].ok;
                }
            }
            CardEntry entry = new CardEntry { pawn = pawn, xform = xform, extra = extra };
            try
            {
                TryArgs[0] = pawn;
                TryArgs[1] = null;
                TryArgs[2] = null;
                if ((bool)cardsTryGet.Invoke(null, TryArgs))
                {
                    entry.xform = (Matrix4x4)TryArgs[1];
                    entry.extra = (Matrix4x4)TryArgs[2];
                    entry.ok = true;
                }
            }
            catch
            {
                entry.ok = false;
            }
            if (cache.Count < 96)
            {
                cache.Add(entry);
            }
            xform = entry.xform;
            extra = entry.extra;
            return entry.ok;
        }

        // The card matrices of the pawn being drawn right now, or false when the pawn is not drawn as a card.
        internal static bool CardMatrices(out Matrix4x4 xform, out Matrix4x4 extra, out Pawn pawn)
        {
            pawn = CurrentCardPawn();
            return CardMatricesFor(pawn, out xform, out extra);
        }

        internal static bool CardMatrices(out Matrix4x4 xform, out Matrix4x4 extra)
        {
            Pawn pawn;
            return CardMatrices(out xform, out extra, out pawn);
        }

        // True when some card pawn of this or the previous frame stands within radius of a world point. Cheap check
        // used before walking the call stack.
        // Where a pawn is actually drawn. IsoRimZ's upper floors sit in far-north row bands in game space and are moved
        // onto their floor only when drawn (IsoRim.Floors.Drawn: z -= shift, y += height/squash). Comparing a raw
        // DrawPos against drawn-space positions therefore reads a second-floor pawn as "far away" - which is exactly
        // why a halo stayed flat upstairs while the ground floor worked.
        internal static Vector3 DrawnPawnPos(Pawn pawn)
        {
            if (pawn == null)
            {
                return Vector3.zero;
            }
            try
            {
                return pawn.DrawPos + Floors.DrawnOffset(pawn);
            }
            catch
            {
                return pawn.DrawPos;
            }
        }

        internal static bool NearAnyCard(Vector3 at, float radius)
        {
            if (NearAnyIn(FramePawns, at, radius))
            {
                return true;
            }
            return NearAnyIn(prevFramePawns, at, radius);
        }

        private static bool NearAnyIn(List<Pawn> list, Vector3 at, float radius)
        {
            if (list == null)
            {
                return false;
            }
            float r2 = radius * radius;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p == null || !p.Spawned)
                {
                    continue;
                }
                Vector3 d = p.DrawPos;
                float dx = d.x - at.x;
                float dz = d.z - at.z;
                if (dx * dx + dz * dz <= r2)
                {
                    return true;
                }
            }
            return false;
        }

        // The card of the pawn drawn this or the previous frame that is closest to a world point (within radius).
        // Used for parts that are drawn outside PawnRenderer.RenderPawnAt, where the renderer's pawn is not known.
        internal static bool NearestCardInFrame(Vector3 at, float radius, out Pawn pawn, out Matrix4x4 xform, out Matrix4x4 extra)
        {
            pawn = null;
            xform = Matrix4x4.identity;
            extra = Matrix4x4.identity;
            float best = radius * radius;
            bool found = false;
            ScanPawns(FramePawns, at, ref best, ref found, ref pawn, ref xform, ref extra);
            if (prevFramePawns != null)
            {
                ScanPawns(prevFramePawns, at, ref best, ref found, ref pawn, ref xform, ref extra);
            }
            return found;
        }

        private static void ScanPawns(List<Pawn> list, Vector3 at, ref float best, ref bool found, ref Pawn pawn, ref Matrix4x4 xform, ref Matrix4x4 extra)
        {
            if (list == null)
            {
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p == null || !p.Spawned)
                {
                    continue;
                }
                Vector3 d = p.DrawPos;
                float dx = d.x - at.x;
                float dz = d.z - at.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > best)
                {
                    continue;
                }
                Matrix4x4 x;
                Matrix4x4 e;
                if (!CardMatricesFor(p, out x, out e))
                {
                    continue;
                }
                best = d2;
                pawn = p;
                xform = x;
                extra = e;
                found = true;
            }
        }

        internal static void NoteRenderPawn(PawnRenderer renderer)
        {
            try
            {
                if (renderer == null)
                {
                    renderPawn = null;
                    return;
                }
                if (rendererPawnField == null)
                {
                    rendererPawnField = AccessTools.Field(typeof(PawnRenderer), "pawn");
                }
                renderPawn = rendererPawnField != null ? rendererPawnField.GetValue(renderer) as Pawn : null;
                if (renderPawn != null)
                {
                    List<Pawn> list = FramePawns;
                    if (list.Count < 96 && !list.Contains(renderPawn))
                    {
                        list.Add(renderPawn);
                    }
                }
            }
            catch
            {
            }
        }

        internal static void ClearRenderPawn()
        {
            renderPawn = null;
        }

        private static bool SeenFromBehind(Pawn pawn)
        {
            try
            {
                return View3D.SeenFacing(pawn.Rotation) == Rot4.North;
            }
            catch
            {
                return false;
            }
        }

        private static Pawn PawnOf(Thing thing)
        {
            if (thing == null)
            {
                return null;
            }
            if (thing is Pawn pawn)
            {
                return pawn;
            }
            if (thing.ParentHolder is Pawn_EquipmentTracker equipment)
            {
                return equipment.pawn;
            }
            if (thing.ParentHolder is Pawn_CarryTracker carry)
            {
                return carry.pawn;
            }
            if (thing.ParentHolder is Pawn_InventoryTracker inventory)
            {
                return inventory.pawn;
            }
            return null;
        }

        // True when a weapon back patch class is inside this call (SYS.dll, or the copies compiled into
        // RatkinGeneExpanded.dll / Epona.dll). Frame 1 is DrawEquipmentAiming itself (Harmony patches at entry), so
        // the whole window has to be scanned.
        private static bool CallerIsWeaponBack()
        {
            try
            {
                System.Diagnostics.StackTrace trace = new System.Diagnostics.StackTrace(1, false);
                for (int i = 0; i < trace.FrameCount && i < 10; i++)
                {
                    MethodBase m = trace.GetFrame(i).GetMethod();
                    if (m == null || m.DeclaringType == null)
                    {
                        continue;
                    }
                    if (m.DeclaringType.Name.StartsWith("DrawEquipment_WeaponBack"))
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

        #endregion
    }

    // Remembers which pawn PawnRenderer is drawing right now, so item draws that happen outside the equipment call can
    // still be given that pawn's card transform.
    [HarmonyPatch]
    public static class RenderPawnCapturePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo m in typeof(PawnRenderer).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == "RenderPawnAt")
                {
                    yield return m;
                }
            }
        }

        private static void Prefix(PawnRenderer __instance)
        {
            Patch.NoteRenderPawn(__instance);
        }

        private static void Finalizer()
        {
            Patch.ClearRenderPawn();
        }
    }
}
