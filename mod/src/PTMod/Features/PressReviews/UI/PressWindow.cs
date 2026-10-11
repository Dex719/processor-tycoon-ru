using System;
using PTMod.Core;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The window "Press" (FR-7, FR-9, design.md "Окно «Пресса»"): a row of tabs (Reviews, Reputation, Awards of the year, Articles) over a scrolling body.
    /// It is made from the shared template <c>Window.Panel</c>, so it does not stop the game, and it is opened from the page of the feature in the hub
    /// (the hub is modal and is closed first) or by a call of <see cref="Open"/>. The tabs are <see cref="ReviewsTab"/>, <see cref="ReputationTab"/>,
    /// <see cref="AwardsTab"/> and <see cref="ArticlesTab"/>; each one is built into the emptied body every time it is shown and reads the live state.
    /// </summary>
    public static class PressWindow
    {
        /// <summary>The tabs of the window, in the order of the tab bar.</summary>
        public enum PressTab
        {
            Reviews = 0,
            Reputation = 1,
            Awards = 2,
            Articles = 3,
        }

        private const string Feature = PressReviewsFeature.FeatureId;
        private const string WindowId = Feature + ".window";
        private const float TabBarHeight = 34f;
        private const float BodyHeight = 500f;
        private const float BodyMinHeight = 200f;

        private static readonly Vector2 WindowSize = new Vector2(1000f, 650f);
        private static readonly string[] TabKeys =
        {
            "press.window.tab.reviews", "press.window.tab.reputation", "press.window.tab.awards", "press.window.tab.articles",
        };

        private static ModWindow window;
        private static RectTransform tabBar;
        private static RectTransform body;
        private static PressTab current = PressTab.Reviews;

        /// <summary>The window is on the screen.</summary>
        public static bool IsOpen
        {
            get { return window != null && window.IsAlive && window.IsOpen; }
        }

        /// <summary>The tab that is shown (or was shown last).</summary>
        public static PressTab CurrentTab
        {
            get { return current; }
        }

        /// <summary>
        /// Opens the window on a tab (made on first use). A window that is open already switches to the tab and comes to the front. Does nothing while
        /// the feature is not active or the templates of the scene are not ready. Never throws.
        /// </summary>
        public static void Open(PressTab tab = PressTab.Reviews)
        {
            ModHooks.Guard(Feature, "ui/window", () => OpenCore(tab));
        }

        /// <summary>Closes the window when it is open.</summary>
        public static void Close()
        {
            if (window != null && window.IsAlive) window.Close();
        }

        /// <summary>The tab asks to be built again (a filter changed, a page was turned); one frame later, so the clicked widget is not destroyed in its own click.</summary>
        internal static void Refresh()
        {
            PressUi.Defer(RebuildBody);
        }

        private static void OpenCore(PressTab tab)
        {
            if (!ModFeatures.IsActive(Feature)) return;
            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the window cannot be made yet, the templates of the scene are not ready");
                return;
            }
            if (!EnsureWindow()) return;

            current = tab;
            RebuildTabBar();
            RebuildBody();
            window.Open();
        }

        private static bool EnsureWindow()
        {
            if (window != null && window.IsAlive && tabBar != null && body != null) return true;

            window = null;
            tabBar = null;
            body = null;
            ModWindow made = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "press.window.title", WindowSize);
            if (made == null) return false;   // ModUI has logged why

            try
            {
                PressUi.Clear(made.Content);   // the id is unique per scene: a window made before (a frame that failed halfway) comes back as it was
                RectTransform bar = PressUi.Row(made.Content, "PTPressTabs", TabBarHeight, 6f);
                RectTransform scroll = PressUi.ScrollBody(made.Content, BodyHeight, BodyMinHeight);
                if (bar == null || scroll == null) throw new InvalidOperationException("the tab bar or the scroll view could not be made");

                window = made;
                tabBar = bar;
                body = scroll;
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "ui: cannot build the frame of the window", ex);
                PressUi.Clear(made.Content);
                return false;
            }
        }

        private static void RebuildTabBar()
        {
            if (tabBar == null) return;

            PressUi.Clear(tabBar);
            for (int i = 0; i < TabKeys.Length; i++)
            {
                var tab = (PressTab)i;
                bool isCurrent = tab == current;
                string caption = ModL10n.L(TabKeys[i]);
                ButtonUI button = ModUI.Button(tabBar, caption, () => Select(tab), isCurrent ? ButtonStyle.CallToAction : ButtonStyle.Standard);
                if (button == null && isCurrent) ModUI.Button(tabBar, caption, () => Select(tab));   // no call-to-action template in the scene: the current tab is not marked
            }
        }

        private static void Select(PressTab tab)
        {
            PressUi.Defer(() =>
            {
                current = tab;
                RebuildTabBar();
                RebuildBody();
            });
        }

        private static void RebuildBody()
        {
            if (body == null || window == null || !window.IsAlive) return;

            PressUi.Clear(body);
            body.anchoredPosition = Vector2.zero;   // a rebuilt tab starts at the top

            RectTransform target = body;
            PressTab tab = current;
            bool built = ModHooks.Guard(Feature, "ui/tab/" + tab, () =>
            {
                switch (tab)
                {
                    case PressTab.Reputation:
                        ReputationTab.Build(target);
                        break;
                    case PressTab.Awards:
                        AwardsTab.Build(target);
                        break;
                    case PressTab.Articles:
                        ArticlesTab.Build(target);
                        break;
                    default:
                        ReviewsTab.Build(target);
                        break;
                }
            });
            if (!built) ModUI.Text(target, ModL10n.L("press.window.tabError"));
        }
    }
}
