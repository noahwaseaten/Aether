using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SmartHunter.Core.Data;

namespace SmartHunter.Core
{
    // What's wrong right now, in plain words, for the card in the Aether window. The log keeps the details.
    // One entry per key: reporting the same key again replaces its text, and Clear removes it once it's fixed.
    public class Problem
    {
        public string Key { get; set; }
        public string Text { get; set; }
        public ICommand DismissCommand { get; set; }
    }

    public class Problems : Bindable
    {
        public static Problems Instance { get; } = new Problems();

        public ObservableCollection<Problem> Items { get; } = new ObservableCollection<Problem>();

        bool m_HasAny;
        public bool HasAny { get { return m_HasAny; } set { SetProperty(ref m_HasAny, value); } }

        public static void Report(string key, string text) => OnUiThread(() =>
        {
            var existing = Instance.Items.FirstOrDefault(p => p.Key == key);
            if (existing != null && existing.Text == text)
            {
                return;
            }
            Log.WriteLine("Problem: " + text);
            var problem = new Problem { Key = key, Text = text };
            problem.DismissCommand = new Command(_ => Instance.Remove(problem));
            if (existing != null)
            {
                Instance.Items[Instance.Items.IndexOf(existing)] = problem;
            }
            else
            {
                Instance.Items.Add(problem);
            }
            Instance.HasAny = true;
        });

        public static void Clear(string key) => OnUiThread(() =>
        {
            var existing = Instance.Items.FirstOrDefault(p => p.Key == key);
            if (existing != null)
            {
                Instance.Remove(existing);
            }
        });

        void Remove(Problem problem)
        {
            Items.Remove(problem);
            HasAny = Items.Count > 0;
        }

        // Failures happen on network and reader threads; the card's list belongs to the UI thread
        static void OnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return; // self-test, or shutting down
            }
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action);
            }
        }
    }
}
