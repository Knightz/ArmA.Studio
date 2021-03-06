﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Documents;
using System.Xml;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Windows.Controls;
using ArmA.Studio.DataContext.TextEditorUtil;
using System.Windows.Controls.Primitives;

namespace ArmA.Studio.DataContext
{
    public class TextEditorDocument : DocumentBase
    {
        private static DataTemplate ThisTemplate { get; set; }
        static TextEditorDocument()
        {
            ThisTemplate = GetDataTemplateFromAssemblyRes("ArmA.Studio.UI.DataTemplates.TextEditorDocumentTemplate.xaml");
        }
        public override string[] SupportedFileExtensions { get { return new string[] { ".txt" }; } }
        public override DataTemplate Template { get { return ThisTemplate; } }
        public virtual IHighlightingDefinition SyntaxDefinition { get { return null; } }


        public override string Title { get { return this.HasChanges ? string.Concat(Path.GetFileName(this.FilePath), '*') : Path.GetFileName(this.FilePath); } }
        public override string FilePath { get { return this._FilePath; } }
        private string _FilePath;
        public void SetFilePath(string title)
        {
            this._FilePath = title;
            this.RaisePropertyChanged("Title");
            this.RaisePropertyChanged("FilePath");
        }

        public TextDocument Document { get { return this._Document; } set { this._Document = value; this.RaisePropertyChanged(); } }
        private TextDocument _Document;

        public ICSharpCode.AvalonEdit.TextEditor Editor { get { return this._Editor; } set { this._Editor = value; this.RaisePropertyChanged(); this.OnTextEditorSet(); } }
        private ICSharpCode.AvalonEdit.TextEditor _Editor;

        public bool CmdKeyDownHandledValue { get { return this._CmdKeyDownHandledValue; } set { this._CmdKeyDownHandledValue = value; this.RaisePropertyChanged(); } }
        private bool _CmdKeyDownHandledValue;


        public int Line { get { return this._Line; } set { this._Line = value; this.RaisePropertyChanged(); } }
        private int _Line;
        public int Column { get { return this._Column; } set { this._Column = value; this.RaisePropertyChanged(); } }
        private int _Column;

        public ICommand CmdTextChanged { get; private set; }
        public ICommand CmdKeyDown { get; private set; }
        public ICommand CmdTextEditorInitialized { get; private set; }
        public ICommand CmdIntelliSensePopupInitialized { get; private set; }
        public ICommand CmdEditorPreviewMouseDown { get; private set; }

        public SolutionUtil.SolutionFileBase SFBRef { get; private set; }

        internal UI.UnderlineBackgroundRenderer SyntaxErrorRenderer { get; private set; }
        public IEnumerable<LinterInfo> LinterInfos { get; private set; }
        public IList<IntelliSenseEntry> IntelliSenseEntries { get { return this._IntelliSenseEntries; } set { this._IntelliSenseEntries = value; this.RaisePropertyChanged(); } }
        public IList<IntelliSenseEntry> _IntelliSenseEntries;
        public IntelliSenseEntry IntelliSenseEntrySelected { get { return this._IntelliSenseEntrySelected; } set { this._IntelliSenseEntrySelected = value; this.RaisePropertyChanged(); } }
        public IntelliSenseEntry _IntelliSenseEntrySelected;

        private ToolTip EditorTooltip;
        private Popup IntelliSensePopup;
        private Task LinterTask;
        private Task WaitTimeoutTask;
        private DateTime LastTextChanged;
        private const int CONST_LINTER_UPDATE_TIMEOUT_MS = 200;

        public TextEditorDocument()
        {

            this.EditorTooltip = new ToolTip();
            this.SyntaxErrorRenderer = new UI.UnderlineBackgroundRenderer();
            this.CmdTextChanged = new UI.Commands.RelayCommand(OnTextChanged);
            this.CmdKeyDown = new UI.Commands.RelayCommand(OnKeyDown);
            this.CmdTextEditorInitialized = new UI.Commands.RelayCommand((p) =>
            {
                this.Editor = p as ICSharpCode.AvalonEdit.TextEditor;
                this.Editor.MouseHover += Editor_MouseHover;
                this.Editor.MouseHoverStopped += Editor_MouseHoverStopped;
                this.Editor.MouseMove += Editor_MouseMove;
                this.Editor.TextArea.TextView.BackgroundRenderers.Add(new UI.LineHighlighterBackgroundRenderer(this.Editor));
                this.Editor.TextArea.TextView.BackgroundRenderers.Add(this.SyntaxErrorRenderer);
                this.Editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
                this.Editor.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;
            });
            this.CmdIntelliSensePopupInitialized = new UI.Commands.RelayCommand((p) =>
            {
                this.IntelliSensePopup = p as Popup;
            });
            this.CmdEditorPreviewMouseDown = new UI.Commands.RelayCommand((p) => this.IntelliSensePopup.IsOpen = false);
            this._Document = new TextDocument();
            this._Document.TextChanged += Document_TextChanged;
            
        }

        private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            if (this.IntelliSensePopup.IsOpen)
            {
                if (Keyboard.IsKeyDown(Key.Tab) || Keyboard.IsKeyDown(Key.Enter))
                {
                    this.Editor.Document.Insert(this.Editor.CaretOffset, this.IntelliSenseEntrySelected.ContentToFinish);
                    this.IntelliSensePopup.IsOpen = false;
                }
                else if (Keyboard.IsKeyDown(Key.Up))
                {
                    var index = this.IntelliSenseEntries.IndexOf(this.IntelliSenseEntrySelected) - 1;
                    this.IntelliSenseEntrySelected = this.IntelliSenseEntries.ElementAt(index < 0 ? 0 : index);
                }
                else if (Keyboard.IsKeyDown(Key.Down))
                {
                    var index = this.IntelliSenseEntries.IndexOf(this.IntelliSenseEntrySelected) + 1;
                    this.IntelliSenseEntrySelected = this.IntelliSenseEntries.ElementAt(index >= this.IntelliSenseEntries.Count ? this.IntelliSenseEntries.Count - 1 : index);
                }
                else
                {
                    this.IntelliSensePopup.IsOpen = false;
                    e.Handled = false;
                }
            }
            else
            {
                e.Handled = false;
            }
        }

        private void Caret_PositionChanged(object sender, EventArgs e)
        {
            this.Line = this.Editor.TextArea.Caret.Line;
            this.Column = this.Editor.TextArea.Caret.Column;
        }

        private void Editor_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(this.Editor);
            var textViewPos = this.Editor.GetPositionFromPoint(pos);
            this.OnMouseMove(textViewPos.HasValue ? this.Document.GetOffset(textViewPos.Value.Location) : -1, pos);
        }

        private async void Editor_MouseHover(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(this.Editor);
            var textViewPos = this.Editor.GetPositionFromPoint(pos);
            if (textViewPos.HasValue)
            {
                var textOffset = this.Document.GetOffset(textViewPos.Value.Location);
                if (await this.OnHoverText(textOffset, pos) || this.LinterInfos == null)
                    return;
                foreach(var info in this.LinterInfos)
                {
                    if(info.StartOffset <= textOffset && info.EndOffset >= textOffset)
                    {
                        this.EditorTooltip.PlacementTarget = this.Editor;
                        this.EditorTooltip.Content = info.Message;
                        this.EditorTooltip.IsOpen = true;
                        break;
                    }
                }
            }
        }
        private void Editor_MouseHoverStopped(object sender, MouseEventArgs e)
        {
            this.EditorTooltip.IsOpen = false;
            this.OnHoverTextStop();
        }
        //ToDo: Fix error offset moving
        private void ExecuteLinter()
        {
            if (this.LinterTask == null || this.LinterTask.IsCompleted)
            {
                var memstream = new MemoryStream();
                var txt = this.Document.Text;
                try
                {
                    
                    //Load content into MemoryStream
                    var writer = new StreamWriter(memstream);
                    writer.Write(txt);
                    writer.Flush();
                    memstream.Seek(0, SeekOrigin.Begin);
                }
                catch
                {
                    memstream.Close();
                    memstream.Dispose();
                }
                this.LinterTask = Task.Run(() =>
                {
                    using (memstream)
                    {
                        var linterInfos = this.GetLinterInformations(memstream);
                        if (linterInfos == null)
                        {
                            return;
                        }
                        SyntaxErrorRenderer.SyntaxErrors = this.LinterInfos = linterInfos;
                        SyntaxErrorRenderer.SyntaxErrors_TextLength = txt.Length;
                        ErrorListPane.Instance.LinterDictionary[this.FilePath] = this.LinterInfos;
                        if (this.Editor != null)
                        {
                            App.Current.Dispatcher.InvokeAsync(() => this.Editor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Selection)).Wait();
                        }
                    }
                });
            }
        }

        private void Document_TextChanged(object sender, EventArgs e)
        {
            this.LastTextChanged = DateTime.Now;
            if (this.WaitTimeoutTask == null || this.WaitTimeoutTask.IsCompleted)
            {
                this.WaitTimeoutTask = Task.Run(() =>
                {
                    System.Threading.SpinWait.SpinUntil(() => (DateTime.Now - this.LastTextChanged).TotalMilliseconds > CONST_LINTER_UPDATE_TIMEOUT_MS);
                    App.Current.Dispatcher.Invoke(() => this.ExecuteLinter());
                });
            }

            if (this.Editor != null)
            {
                if (this.Editor.Document.GetWordAround(this.Editor.CaretOffset - 1).Length >= 3)
                {
                    this.ShowIntelliSense();
                }
                else
                {
                    this.IntelliSensePopup.IsOpen = false;
                }
            }
        }


        private void ShowIntelliSense()
        {
            if (this.Editor == null)
                return;
            string curWord = this.Editor.Document.GetWordAround(this.Editor.CaretOffset - 1);
            this.IntelliSenseEntries = this.GetIntelliSenseEntries(this.Document, curWord, this.Editor.CaretOffset);
            this.IntelliSenseEntrySelected = this.IntelliSenseEntries.FirstOrDefault();
            if (this.IntelliSenseEntries.Count > 0)
            {
                this.IntelliSensePopup.DataContext = this;

                var pos = this.Editor.TextArea.TextView.GetVisualPosition(this.Editor.TextArea.Caret.Position, ICSharpCode.AvalonEdit.Rendering.VisualYPosition.TextBottom);
                this.IntelliSensePopup.PlacementTarget = this.Editor;
                this.IntelliSensePopup.Placement = PlacementMode.Relative;
                this.IntelliSensePopup.HorizontalOffset = pos.X + 10+18*2;
                this.IntelliSensePopup.VerticalOffset = pos.Y;
                this.IntelliSensePopup.IsOpen = true;
            }
        }

        public override void SaveDocument(string path)
        {
            this.HasChanges = false;
            this.RaisePropertyChanged("Title");
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using (var writer = new StreamWriter(File.Open(path, FileMode.Create)))
            {
                writer.Write(this.Document.Text);
                writer.Flush();
            }
        }
        public override void OpenDocument(string path)
        {
            this.SFBRef = Workspace.CurrentWorkspace.CurrentSolution.GetOrCreateFileReference(path);
            this.SetFilePath(path);
            if(!File.Exists(path))
            {
                MessageBox.Show(string.Format(Properties.Localization.FileNotFound, path), Properties.Localization.Error, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            using (var stream = File.OpenRead(path))
            {
                this.Document.Text = new StreamReader(stream).ReadToEnd();
                this.Document.UndoStack.ClearAll();
            }
        }
        public override void ReloadDocument()
        {
            this.OpenDocument(this.FilePath);
        }

        protected static IHighlightingDefinition LoadAvalonEditSyntaxFiles(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var xshd = HighlightingLoader.LoadXshd(XmlReader.Create(stream));
                    var highlightDef = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
                    HighlightingManager.Instance.RegisterHighlighting(xshd.Name, xshd.Extensions.ToArray(), highlightDef);
                    return highlightDef;
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Localization.XSHD_Loading_Error_Body, path, ex.GetType(), ex.Message), Properties.Localization.XSHD_Loading_Error_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }


        protected virtual void OnTextChanged(object param)
        {
            this.HasChanges = true;
            this.RaisePropertyChanged("Title");
        }
        protected virtual void OnTextEditorSet() { }
        protected virtual void OnKeyDown(object param)
        {
            if (Keyboard.IsKeyDown(Key.S) && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                this.CmdKeyDownHandledValue = true;
                this.SaveDocument(this.FilePath);
            }
            else if (Keyboard.IsKeyDown(Key.Space) && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                this.CmdKeyDownHandledValue = true;
                this.ShowIntelliSense();
            }
        }
        protected virtual IEnumerable<LinterInfo> GetLinterInformations(MemoryStream memstream)
        {
            return null;
        }
        protected virtual IList<IntelliSenseEntry> GetIntelliSenseEntries(TextDocument currentDocument, string currentWord, int caretOffset)
        {
            return new IntelliSenseEntry[0];
        }

        /// <summary>
        /// Callen when user hovers above text.
        /// </summary>
        /// <param name="textOffset">offset where the user is hovering.</param>
        /// <param name="p">Point where the mouse is relative to <paramref name="placementTarget"/></param>
        /// <returns><see cref="true"/> if the document has handled the hover, false if it did not.</returns>
        protected virtual async Task<bool> OnHoverText(int textOffset, Point p) { return false; }
        protected virtual void OnMouseMove(int textOffset, Point p) { }
        protected virtual void OnHoverTextStop() { }
    }
}