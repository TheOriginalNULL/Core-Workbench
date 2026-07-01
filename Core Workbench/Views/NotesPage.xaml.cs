using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Core_Workbench.Models;
using Core_Workbench.Services;
using Microsoft.Win32;

namespace Core_Workbench.Views
{
    /// <summary>
    /// Rich-text notes: a list + a RichTextBox editor with a formatting toolbar
    /// (headings, bold/italic/underline, lists, colours, highlight, tables,
    /// alignment) plus a read mode. Content is stored as FlowDocument XAML so all
    /// formatting round-trips; edits autosave after a short idle delay.
    /// </summary>
    public partial class NotesPage : UserControl
    {
        private readonly NotesService _service = new();
        private readonly List<Note> _notes;                       // full set (persisted)
        private readonly ObservableCollection<Note> _view = new(); // filtered/sorted, shown
        private readonly DispatcherTimer _saveTimer;
        private Note? _current;
        private bool _loadingEditor;     // suppress change events while populating
        private bool _suppressSelection; // ignore programmatic selection changes
        private bool _readMode;

        public NotesPage()
        {
            InitializeComponent();

            _notes = _service.Load().ToList();
            NoteList.ItemsSource = _view;

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                CommitCurrent();
                PersistAll();
                SaveStatus.Text = $"Saved {DateTime.Now:HH:mm:ss}";
            };

            RebuildView();
            if (_view.Count > 0) NoteList.SelectedIndex = 0;
            else ShowEditor(false);

            Unloaded += (_, _) =>
            {
                _saveTimer.Stop();
                CommitCurrent();
                PersistAll();
            };
        }

        // ============ note list / search / sort ============

        private void Search_TextChanged(object sender, TextChangedEventArgs e) => RebuildView();

        /// <summary>Filter by search text and sort pinned-first, then most-recent.</summary>
        private void RebuildView()
        {
            string q = SearchBox?.Text?.Trim() ?? "";
            IEnumerable<Note> items = _notes;
            if (!string.IsNullOrEmpty(q))
                items = items.Where(n => n.DisplayTitle.Contains(q, StringComparison.OrdinalIgnoreCase)
                                         || (n.Body ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            items = items.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.Updated);

            _suppressSelection = true;
            _view.Clear();
            foreach (var n in items) _view.Add(n);
            if (_current != null && _view.Contains(_current)) NoteList.SelectedItem = _current;
            _suppressSelection = false;
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            CommitCurrent();
            var note = new Note { Title = "Untitled" };
            _notes.Add(note);
            _current = note;
            RebuildView();
            NoteList.SelectedItem = note;
            TitleBox.Focus();
            TitleBox.SelectAll();
        }

        private void NoteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection) return;
            CommitCurrent();

            _current = NoteList.SelectedItem as Note;
            if (_current == null) { ShowEditor(false); return; }

            ShowEditor(true);
            _loadingEditor = true;
            TitleBox.Text = _current.Title;
            Editor.Document = LoadDocument(_current);
            _loadingEditor = false;
            UpdatePinFavButton();
            SaveStatus.Text = $"Edited {_current.Updated:MMM d, HH:mm}";
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            if (MessageBox.Show($"Delete \"{_current.DisplayTitle}\"?", "Confirm delete",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            _notes.Remove(_current);
            _current = null;
            PersistAll();
            RebuildView();

            if (_view.Count > 0) NoteList.SelectedIndex = 0;
            else ShowEditor(false);
        }

        // ============ pin / favorite, export, print ============

        private void PinFav_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            _current.Pinned = !_current.Pinned;
            UpdatePinFavButton();
            PersistAll();
            RebuildView();
        }

        private void UpdatePinFavButton()
            => PinFavButton.Content = _current?.Pinned == true ? "★ Pinned" : "☆ Pin";

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            CommitCurrent();
            var dlg = new SaveFileDialog
            {
                Filter = "Rich Text (*.rtf)|*.rtf",
                FileName = SafeFileName(_current.DisplayTitle) + ".rtf",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                using var fs = File.Create(dlg.FileName);
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                range.Save(fs, DataFormats.Rtf);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't export: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            CommitCurrent();
            var pd = new PrintDialog();
            if (pd.ShowDialog() != true) return;
            try
            {
                // Print a copy so the editor's layout isn't disturbed.
                var doc = (FlowDocument)XamlReader.Parse(XamlWriter.Save(Editor.Document));
                doc.PageWidth = pd.PrintableAreaWidth;
                doc.PagePadding = new Thickness(48);
                doc.ColumnWidth = pd.PrintableAreaWidth;
                pd.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, _current.DisplayTitle);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't print: {ex.Message}", "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrWhiteSpace(s) ? "note" : s;
        }

        // ============ autosave ============

        private void Editor_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingEditor || _current == null) return;
            SaveStatus.Text = "Saving…";
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void CommitCurrent()
        {
            if (_current == null) return;
            _current.Title = TitleBox.Text;
            _current.DocumentXaml = XamlWriter.Save(Editor.Document);
            _current.Body = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
            _current.Updated = DateTime.Now;
            NoteList.Items.Refresh();
        }

        private void PersistAll()
        {
            _service.Save(_notes);
            WidgetManager.RefreshAll();   // keep any pinned desktop widgets in sync
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            CommitCurrent();
            PersistAll();
            WidgetManager.Open(_current.Id);
        }

        // ============ document load ============

        private static FlowDocument LoadDocument(Note note)
        {
            if (!string.IsNullOrEmpty(note.DocumentXaml))
            {
                try { return (FlowDocument)XamlReader.Parse(note.DocumentXaml); }
                catch { /* fall through to plain */ }
            }
            return PlainDocument(note.Body);
        }

        private static FlowDocument PlainDocument(string text)
        {
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run(text ?? string.Empty)));
            return doc;
        }

        // ============ toolbar: formatting ============

        private void Heading_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b) return;
            (double size, FontWeight weight) = (string)b.Tag switch
            {
                "1" => (24.0, FontWeights.Bold),
                "2" => (19.0, FontWeights.Bold),
                _ => (14.0, FontWeights.Normal),
            };
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, weight);
            Editor.Focus();
        }

        private void Strike_Click(object sender, RoutedEventArgs e)
        {
            var current = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            bool has = current is TextDecorationCollection tdc &&
                       tdc.Any(d => d.Location == TextDecorationLocation.Strikethrough);
            Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
                has ? null : TextDecorations.Strikethrough);
            Editor.Focus();
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b) return;
            var color = (Color)ColorConverter.ConvertFromString((string)b.Tag);
            Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
            Editor.Focus();
        }

        private void Highlight_Click(object sender, RoutedEventArgs e)
        {
            var current = Editor.Selection.GetPropertyValue(TextElement.BackgroundProperty);
            bool has = current is Brush;
            Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty,
                has ? null : new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xD9, 0x6B)));
            Editor.Focus();
        }

        // ============ toolbar: table ============

        private void Table_Click(object sender, RoutedEventArgs e)
        {
            var table = BuildTable(rows: 3, cols: 3);

            var caretPara = Editor.CaretPosition.Paragraph;
            if (caretPara != null && caretPara.Parent is FlowDocument)
            {
                Editor.Document.Blocks.InsertAfter(caretPara, table);
                Editor.Document.Blocks.InsertAfter(table, new Paragraph());
            }
            else
            {
                Editor.Document.Blocks.Add(table);
                Editor.Document.Blocks.Add(new Paragraph());
            }

            Editor_Changed(this, e);
            Editor.Focus();
        }

        private static Table BuildTable(int rows, int cols)
        {
            var border = new SolidColorBrush(Color.FromRgb(0x38, 0x25, 0x62));
            var headerBg = new SolidColorBrush(Color.FromRgb(0x2C, 0x1D, 0x52));

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 6) };
            for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());

            var group = new TableRowGroup();

            // Header row
            var header = new TableRow();
            for (int c = 0; c < cols; c++)
                header.Cells.Add(MakeCell($"Header {c + 1}", border, headerBg, bold: true));
            group.Rows.Add(header);

            // Body rows
            for (int r = 1; r < rows; r++)
            {
                var row = new TableRow();
                for (int c = 0; c < cols; c++)
                    row.Cells.Add(MakeCell(string.Empty, border, null, bold: false));
                group.Rows.Add(row);
            }

            table.RowGroups.Add(group);
            return table;
        }

        private static TableCell MakeCell(string text, Brush border, Brush? background, bool bold)
        {
            var run = new Run(text);
            if (bold) run.FontWeight = FontWeights.Bold;
            return new TableCell(new Paragraph(run))
            {
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 4, 7, 4),
                Background = background,
            };
        }

        // ============ read mode ============

        private void ReadToggle_Click(object sender, RoutedEventArgs e)
        {
            _readMode = !_readMode;
            Editor.IsReadOnly = _readMode;
            Toolbar.Visibility = _readMode ? Visibility.Collapsed : Visibility.Visible;
            ReadToggle.Content = _readMode ? "Edit mode" : "Read mode";
        }

        // ============ helpers ============

        private void ShowEditor(bool visible)
        {
            EditorCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
