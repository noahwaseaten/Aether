using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.AccessControl;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using SmartHunter.Core;
using SmartHunter.Core.Helpers;
using SmartHunter.Core.Windows;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Data.WidgetContexts;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game
{
    public class MhwOverlay : Overlay
    {
        MhwMemoryUpdater m_MemoryUpdater;
        bool m_HideKeyDown, m_EditKeyDown;

        public MhwOverlay(Window mainWindow, params WidgetWindow[] widgetWindows) : base(mainWindow, widgetWindows)
        {
            ConfigHelper.Main.Loaded += (s, e) => { UpdateWidgetsFromConfig(); OverlayViewModel.Instance.ApplyDisplaySettings(); };
            OverlayViewModel.Instance.ApplyDisplaySettings();
            WidgetWindow.PlacementChanged += SaveMovedWidgets;
            WidgetWindow.HideRequested += ToggleWidget;
            ConfigHelper.Localization.Loaded += (s, e) => { RefreshWidgetsLayout(); };
            ConfigHelper.MonsterData.Loaded += (s, e) => { RefreshWidgetsLayout(); };
            ConfigHelper.PlayerData.Loaded += (s, e) => { RefreshWidgetsLayout(); };

            OverlayViewModel.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OverlayViewModel.CanManipulateWindows))
                {
                    ApplyEditMode(OverlayViewModel.Instance.CanManipulateWindows);
                }
            };

            if (!ConfigHelper.Main.Values.Debug.UseSampleData)
            {
                m_MemoryUpdater = new MhwMemoryUpdater();
            }
        }

        protected override bool IsEditing => OverlayViewModel.Instance.CanManipulateWindows;

        // Editing: the game dims behind the layout editor and the widgets take the mouse (drag, resize, hide).
        // Done: back to click-through, and save where they ended up.
        void ApplyEditMode(bool isEditing)
        {
            Log.WriteLine(isEditing ? "Layout editor opened" : "Layout editor closed");
            if (isEditing)
            {
                // The debug widget is for development; it's shown from its setting only
                LayoutEditor.Instance.Open(WidgetWindows.Where(w => w.Widget != OverlayViewModel.Instance.DebugWidget), ToggleWidget,
                    () => OverlayViewModel.Instance.ResetLayout(), () => OverlayViewModel.Instance.CanManipulateWindows = false);
            }
            else
            {
                LayoutEditor.Instance.Close();
            }

            // After the editor opens, so the widgets stack above it
            foreach (var widgetWindow in WidgetWindows)
            {
                if (isEditing)
                {
                    WindowHelper.SetTopMostSelectable(widgetWindow as Window);
                }
                else
                {
                    WindowHelper.SetTopMostTransparent(widgetWindow as Window);
                }
            }

            if (isEditing)
            {
                LayoutEditor.Instance.RaiseToolbar();
            }
            else
            {
                // Wait for the widgets to shrink back from their edit-mode size first: right-side ones shift as they
                // shrink, and saving straight away stored the shifted spot, so they crept left every edit
                Application.Current.Dispatcher.BeginInvoke(new Action(SaveMovedWidgets), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        void ToggleWidget(WidgetWindow widgetWindow)
        {
            widgetWindow.Widget.IsVisible = !widgetWindow.Widget.IsVisible;
            UpdateWidgetsFromConfig();
            LayoutEditor.Instance.RaiseToolbar();
            SaveMovedWidgets();
        }

        void SaveMovedWidgets()
        {
            bool canSaveConfig = false;
            foreach (var widgetWindow in WidgetWindows)
            {
                canSaveConfig |= widgetWindow.Widget.CanSaveConfig;
                widgetWindow.Widget.CanSaveConfig = false;
            }

            if (canSaveConfig)
            {
                ConfigHelper.Main.Save();
            }
        }

        protected override void InputReceived(Key key, bool isDown)
        {
            if (key == Key.Escape && isDown && OverlayViewModel.Instance.CanManipulateWindows)
            {
                OverlayViewModel.Instance.CanManipulateWindows = false;
                return;
            }

            foreach (var controlKeyPair in ConfigHelper.Main.Values.Keybinds.Where(keybind => keybind.Value == key).ToList())
            {
                HandleControl(controlKeyPair.Key, isDown);
            }
        }

        private void HandleControl(InputControl control, bool isDown)
        {
            if (control == InputControl.ManipulateWidget)
            {
                // A press opens or closes the layout editor, like Discord's overlay key and Lunar's HUD editor.
                // Holding the key repeats "down" events, so only the first one counts.
                if (isDown && !m_EditKeyDown)
                {
                    OverlayViewModel.Instance.CanManipulateWindows = !OverlayViewModel.Instance.CanManipulateWindows;
                }
                m_EditKeyDown = isDown;
            }
            else if (control == InputControl.HideWidgets)
            {
                // Toggle mode flips on the press only; holding the key repeats "down" events
                if (!ConfigHelper.Main.Values.Overlay.HideKeyToggles)
                {
                    OverlayViewModel.Instance.HideWidgetsRequested = isDown;
                }
                else if (isDown && !m_HideKeyDown)
                {
                    OverlayViewModel.Instance.HideWidgetsRequested = !OverlayViewModel.Instance.HideWidgetsRequested;
                }
                m_HideKeyDown = isDown;
            }
            else if (control == InputControl.CopyTeamDamage && isDown)
            {
                string str = "";
                List<Player> players = OverlayViewModel.Instance.TeamWidget.Context.Players.ToList();
                if (OverlayViewModel.Instance.TeamWidget.Context.Players.Count > 0)
                {
                    players.Sort();
                    foreach (var player in players)
                        str += String.Format("{0} {1}% ", player.Name.Length >= 3 ? player.Name.Substring(0, 3) : player.Name, Math.Round(player.DamageFraction * 100).ToString());
                }
                else
                    str = "No Team Damage";
                CopyToClipboard(str);
            }
            else if (control == InputControl.CopyPlayer1Damage && isDown)
            {
                GetPlayerDamage(OverlayViewModel.Instance.TeamWidget.Context.Players, 1);
            }
            else if (control == InputControl.CopyPlayer2Damage && isDown)
            {
                GetPlayerDamage(OverlayViewModel.Instance.TeamWidget.Context.Players, 2);
            }
            else if (control == InputControl.CopyPlayer3Damage && isDown)
            {
                GetPlayerDamage(OverlayViewModel.Instance.TeamWidget.Context.Players, 3);
            }
            else if (control == InputControl.CopyPlayer4Damage && isDown)
            {
                GetPlayerDamage(OverlayViewModel.Instance.TeamWidget.Context.Players, 4);
            }
        }

        private void GetPlayerDamage(Collection<Player> players, int index)
        {
            List<Player> mplayer = players.ToList();
            mplayer.Sort();
            if (mplayer.Count >= index)
                CopyToClipboard(mplayer[index - 1].ToString());
            else
                Log.WriteLine($"No player {index} in this hunt; clipboard left as it was");
        }

        private void CopyToClipboard(String str)
        {
            // Another app holding the clipboard open makes SetText throw (CLIPBRD_E_CANT_OPEN)
            try
            {
                Clipboard.SetDataObject(str, true);
                Log.WriteLine("Copied to clipboard: " + str);
            }
            catch (Exception ex)
            {
                Log.WriteLine("Couldn't copy to the clipboard, another app is using it: " + ex.Message);
            }
        }
    }
}
