using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Sector.Core;

namespace Sector.Pages
{
    /// <summary>
    /// A page builds its layout once for the shape of the data (which drives, which sensors) and then
    /// only updates numbers on each reading, so nothing flickers and scrolling stays put.
    /// </summary>
    public abstract class PageBase
    {
        public HubWindow Host { get; set; }
        public abstract string Title { get; }
        public virtual bool MachineSpecific => true;
        public virtual string Subtitle(MachineState m) => null;

        protected readonly Border Root = new Border();
        private readonly List<Action<MachineState>> _binders = new List<Action<MachineState>>();
        private string _signature;

        public UIElement Build(MachineState m)
        {
            _signature = null;
            Root.Child = null;
            return Root;
        }

        public virtual void Update(MachineState m)
        {
            string sig = Signature(m);
            if (sig != _signature)
            {
                _signature = sig;
                _binders.Clear();
                Root.Child = Compose(m);
            }
            foreach (var b in _binders)
            {
                try { b(m); } catch (Exception ex) { Log.Write(GetType().Name + " update: " + ex.Message); }
            }
        }

        /// <summary>Forces the next update to rebuild the layout.</summary>
        protected void Invalidate() => _signature = null;

        public virtual void Leave() { }

        protected abstract string Signature(MachineState m);
        protected abstract UIElement Compose(MachineState m);
        protected void Bind(Action<MachineState> update) => _binders.Add(update);

        // ---------- Shared building blocks ----------

        protected static UIElement Section(string title, UIElement body, UIElement right = null)
        {
            var head = new Grid { Margin = new Thickness(2, 0, 0, 12) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = UI.Text(title, 18, Theme.Text, FontWeights.SemiBold, true);
            t.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(t);
            if (right != null)
            {
                Grid.SetColumn(right, 1);
                head.Children.Add(right);
            }
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 28) };
            sp.Children.Add(head);
            sp.Children.Add(body);
            return sp;
        }

        protected static UIElement Empty(string title, string text, string icon = null)
        {
            var sp = UI.V(8);
            sp.HorizontalAlignment = HorizontalAlignment.Center;
            if (icon != null)
            {
                var i = UI.GridIcon(icon, 36, Theme.Muted, 1.5);
                i.HorizontalAlignment = HorizontalAlignment.Center;
                i.Margin = new Thickness(0, 0, 0, 6);
                sp.Children.Add(i);
            }
            var t = UI.Text(title, 17, Theme.Text, FontWeights.SemiBold);
            t.TextAlignment = TextAlignment.Center;
            sp.Children.Add(t);
            var d = UI.Text(text, 13, Theme.Muted);
            d.TextAlignment = TextAlignment.Center;
            d.MaxWidth = 520;
            sp.Children.Add(d);
            return UI.Card(new Border { Padding = new Thickness(20, 40, 20, 40), Child = sp });
        }

        /// <summary>A small rounded label, coloured for good, warning or bad.</summary>
        protected static Border Pill(string text, Brush tone)
        {
            var c = tone is SolidColorBrush sb ? sb.Color : Colors.Gray;
            var b = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 2, 9, 3),
                Background = Theme.Brush(Color.FromArgb(0x26, c.R, c.G, c.B)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = UI.Text(text, 12, tone, FontWeights.SemiBold)
            };
            ((TextBlock)b.Child).TextWrapping = TextWrapping.NoWrap;
            return b;
        }

        protected static void SetPill(Border pill, string text, Brush tone)
        {
            var c = tone is SolidColorBrush sb ? sb.Color : Colors.Gray;
            pill.Background = Theme.Brush(Color.FromArgb(0x26, c.R, c.G, c.B));
            var t = (TextBlock)pill.Child;
            t.Text = text;
            t.Foreground = tone;
        }

        /// <summary>A label on the left and a value on the right.</summary>
        protected static (Grid row, TextBlock value) Row(string label, string value = "", double size = 13)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = UI.Text(label, size, Theme.Muted);
            l.TextWrapping = TextWrapping.NoWrap;
            l.TextTrimming = TextTrimming.CharacterEllipsis;
            l.Margin = new Thickness(0, 0, 12, 0);
            var v = UI.Text(value, size, Theme.Text, FontWeights.SemiBold);
            v.TextWrapping = TextWrapping.NoWrap;
            v.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(v, 1);
            g.Children.Add(l);
            g.Children.Add(v);
            return (g, v);
        }
    }

    /// <summary>A grid that picks how many columns fit, so cards reflow as the window is resized.</summary>
    public class FlowGrid : UniformGrid
    {
        public double MinItemWidth { get; set; } = 260;
        public int MaxColumns { get; set; } = 6;
        public double Gap { get; set; } = 14;

        public FlowGrid(double minItemWidth, int maxColumns = 6)
        {
            MinItemWidth = minItemWidth;
            MaxColumns = maxColumns;
            Columns = Math.Min(2, maxColumns);
            Margin = new Thickness(-Gap / 2);
            SizeChanged += (s, e) => Reflow();
            Loaded += (s, e) => Reflow();
        }

        public void Add(UIElement e)
        {
            if (e is FrameworkElement fe) fe.Margin = new Thickness(Gap / 2);
            Children.Add(e);
            Reflow();
        }

        private void Reflow()
        {
            if (ActualWidth <= 0) return;
            int cols = Math.Max(1, Math.Min(MaxColumns, (int)((ActualWidth + Gap) / (MinItemWidth + Gap))));
            if (Columns != cols) Columns = cols;
        }
    }
}
