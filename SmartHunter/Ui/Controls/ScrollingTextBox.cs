using System;
using System.Windows.Controls;

namespace SmartHunter.Ui.Controls
{
    /// <summary>
    /// This textbox will scroll to end when text is changed
    /// </summary>
    public class ScrollingTextBox : TextBox {

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        }

        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            // Layout hasn't caught up with the new text yet, so these still describe the old view
            bool wasAtEnd = VerticalOffset + ViewportHeight >= ExtentHeight - 2;
            base.OnTextChanged(e);
            // Follow new lines only when already at the bottom; don't yank someone reading further up
            if (wasAtEnd)
            {
                CaretIndex = Text.Length;
                ScrollToEnd();
            }
        }
    }
}
