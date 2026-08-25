using UnityEngine.SceneManagement;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Wrappers for game APIs that changed or throw on null.
    /// Keeps update breakage in one file instead of every call site.
    /// </summary>
    public static class GameCompat
    {
        /// <summary>
        /// True when a gameplay level is loaded, false in the menus.
        /// IsIngame() gained an out param and now derefs GameManager.instance,
        /// which doesn't exist yet during early startup.
        /// </summary>
        public static bool IsIngame()
        {
            if (GameManager.instance != null)
            {
                try
                {
                    return GameManager.IsIngame(out _);
                }
                catch { }
            }

            // Same test the game applies once its manager exists
            if (GameManager.IsInCustomLevel())
                return true;

            return SceneManager.GetActiveScene().buildIndex > 2;
        }

        /// <summary>
        /// True while the loadout screen is showing.
        /// LoadoutUi.IsOpen() derefs its instance and canvas unchecked, so it
        /// throws during scene transitions. The canvas field is private, so the
        /// call is guarded rather than reimplemented.
        /// </summary>
        public static bool IsLoadoutOpen()
        {
            if (LoadoutUi.instance == null) return false;

            try
            {
                return LoadoutUi.IsOpen();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True while the pause menu is showing. Same null hazard, but this
        /// canvas field is public so it can be checked directly.
        /// </summary>
        public static bool IsIngameMenuOpen()
        {
            var instance = IngameMenuUi.instance;
            return instance != null
                && instance.canvas != null
                && instance.canvas.enabled;
        }
    }
}
