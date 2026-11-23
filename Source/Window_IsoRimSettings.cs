using UnityEngine;
using Verse;

namespace IsoRimWeaponCards
{
    // Floating copy of this mod's settings: draggable (drag anywhere on the window), resizable, does not block the
    // game, and remembers where it was last left. The dialog the game builds from the options menu is fixed-size and
    // centred, which is what made tuning awkward; this one can be parked anywhere and reopened from the little icon
    // in the play-settings row.
    public class Window_IsoRimSettings : Window
    {
        public Window_IsoRimSettings()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = false;
            forcePause = false;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
            doWindowBackground = true;
            shadowAlpha = 0.6f;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(920f, 720f); }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            try
            {
                WeaponCardSettings s = WeaponCardMod.Settings;
                if (s != null && s.windowX > -0.5f && s.windowY > -0.5f)
                {
                    windowRect = new Rect(s.windowX, s.windowY, windowRect.width, windowRect.height).Rounded();
                }
            }
            catch
            {
            }
        }

        public override void PreClose()
        {
            base.PreClose();
            try
            {
                WeaponCardSettings s = WeaponCardMod.Settings;
                if (s != null)
                {
                    s.windowX = windowRect.x;
                    s.windowY = windowRect.y;
                }
            }
            catch
            {
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            WeaponCardMod mod = WeaponCardMod.Instance;
            if (mod == null)
            {
                return;
            }
            mod.DoSettingsWindowContents(inRect);
        }
    }
}
