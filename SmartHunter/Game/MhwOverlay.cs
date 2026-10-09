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
        bool m_HideKeyDown;

        public MhwOverlay(Window mainWindow, params WidgetWindow[] widgetWindows) : base(mainWindow, widgetWindows)
        {
            ConfigHelper.Main.Loaded += (s, e) => { UpdateWidgetsFromConfig(); OverlayViewModel.Instance.ApplyDisplaySettings(); };
            OverlayViewModel.Instance.ApplyDisplaySettings();
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

        // Editing: widgets take the mouse (drag, scroll to scale). Done: back to click-through, and save where they ended up.
        void ApplyEditMode(bool isEditing)
        {
            bool canSaveConfig = false;
            foreach (var widgetWindow in WidgetWindows)
            {
                if (isEditing)
                {
                    WindowHelper.SetTopMostSelectable(widgetWindow as Window);
                }
                else
                {
                    WindowHelper.SetTopMostTransparent(widgetWindow as Window);
                    canSaveConfig |= widgetWindow.Widget.CanSaveConfig;
                    widgetWindow.Widget.CanSaveConfig = false;
                }
            }

            if (canSaveConfig)
            {
                ConfigHelper.Main.Save();
            }
        }

        protected override void InputReceived(Key key, bool isDown)
        {
            foreach (var controlKeyPair in ConfigHelper.Main.Values.Keybinds.Where(keybind => keybind.Value == key))
            {
                HandleControl(controlKeyPair.Key, isDown);
            }
        }

        private void HandleControl(InputControl control, bool isDown)
        {
            if (control == InputControl.ManipulateWidget && OverlayViewModel.Instance.CanManipulateWindows != isDown)
            {
                OverlayViewModel.Instance.CanManipulateWindows = isDown;
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
                CopyToClipboard(String.Format("Player Not Found (Index: {0})", index));
        }

        private void CopyToClipboard(String str)
        {
            Clipboard.SetText(str);
            Log.WriteLine("Copy to clipboard > " + str);
        }
    }
}
