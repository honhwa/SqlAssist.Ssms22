using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Task = System.Threading.Tasks.Task;

namespace SqlAssist.Ssms22.UI
{
    /// <summary>
    /// SQL Prompt–style inline variable rename.
    ///
    /// F2 with the caret on a @variable:
    ///   • All occurrences in the current GO batch are highlighted with a
    ///     semi-transparent blue background.
    ///   • The occurrence at the caret is selected so the user can start
    ///     typing immediately.
    ///   • Every keystroke is intercepted and synchronised to all other
    ///     highlighted occurrences in real time.
    ///   • Enter commits, Escape rolls back the whole rename.
    /// </summary>
    internal sealed class InlineRenameCommand
    {
        // Command ID must match cmdidRenameVariable in VSCommandTable.vsct
        // (0x0112). The VSCT-declared ID is the single source of truth: the
        // menu button, toolbar placement and F2 key binding all reference it.
        public const int CommandId = 0x21C;
        public static readonly Guid CommandSet = new Guid("4a188946-9364-4f07-af7e-97f3bd7ca7a7");

        private readonly AsyncPackage _package;
        private InlineRenameSession _currentSession;

        private InlineRenameCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package
                ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService
                ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static InlineRenameCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService))
                    as OleMenuCommandService;
            Instance = new InlineRenameCommand(package, commandService);
        }

        // ── Execute ─────────────────────────────────────────────────────

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                IWpfTextView textView = GetActiveWpfTextView();
                if (textView == null) return;

                int caretPos = textView.Caret.Position.BufferPosition.Position;
                string fullText = textView.TextBuffer.CurrentSnapshot.GetText();

                // 1. Find variable at cursor via ScriptDom tokenizer
                var varInfo = FindVariableAtPosition(fullText, caretPos);
                if (varInfo == null) return;

                string varName = varInfo.Value.name;
                int varOffset = varInfo.Value.offset;
                int varLength = varInfo.Value.length;

                // 2. Find batch boundaries (GO-delimited)
                var (batchStart, batchEnd) = FindBatchBoundaries(fullText, caretPos);

                // 3. Find all occurrences in the batch
                var occurrences = FindAllVariableTokens(
                    fullText, varName, batchStart, batchEnd);

                if (occurrences.Count < 1) return;

                // 4. Determine which occurrence is the cursor one
                int cursorIndex = FindCursorOccurrenceIndex(
                    occurrences, varOffset);

                // 5. Start the rename session
                _currentSession?.Dispose();
                _currentSession = new InlineRenameSession(
                    textView, varName, occurrences, cursorIndex,
                    batchStart, batchEnd);
                _currentSession.BeginRename();
            }
            catch (Exception ex)
            {
                //ignore
                //Infrastructure.OutputWindowLogger.LogError("InlineRename failed", ex);
            }
        }

        // ── Variable detection (ScriptDom tokenizer) ────────────────────

        /// <summary>
        /// Uses ScriptDom's TSql160Parser to tokenize and locate the
        /// @variable (or @@global) token at the caret position.
        /// </summary>
        private static (string name, int offset, int length)?
            FindVariableAtPosition(string fullText, int caretPos)
        {
            try
            {
                var parser = new TSql160Parser(true);
                using (var reader = new StringReader(fullText))
                {
                    IList<ParseError> errors;
                    var fragment = parser.Parse(reader, out errors);

                    if (fragment?.ScriptTokenStream == null) return null;

                    foreach (var token in fragment.ScriptTokenStream)
                    {
                        // Match the token that covers the caret
                        if (caretPos >= token.Offset &&
                            caretPos <= token.Offset + token.Text.Length)
                        {
                            if (token.TokenType == TSqlTokenType.Variable)
                            {
                                return (token.Text, token.Offset,
                                    token.Text.Length);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //Infrastructure.OutputWindowLogger.LogError("FindVariableAtPosition parser error", ex);
            }

            return null;
        }

        /// <summary>
        /// Scans ScriptDom tokens for all occurrences of the variable
        /// inside the given batch range, excluding EXEC parameter names
        /// (left-hand side of <c>@param = @var</c> in procedure calls).
        /// </summary>
        private static List<(int offset, int length)> FindAllVariableTokens(
            string fullText, string variableName,
            int batchStart, int batchEnd)
        {
            var occurrences = new List<(int, int)>();

            try
            {
                var parser = new TSql160Parser(true);
                using (var reader = new StringReader(fullText))
                {
                    IList<ParseError> errors;
                    var fragment = parser.Parse(reader, out errors);

                    if (fragment?.ScriptTokenStream == null)
                        return occurrences;

                    // Build a set of token offsets that are EXEC parameter
                    // names — these must NOT be renamed.
                    var execParamOffsets = FindExecParameterNameOffsets(
                        fragment.ScriptTokenStream);

                    foreach (var token in fragment.ScriptTokenStream)
                    {
                        if (token.Offset < batchStart ||
                            token.Offset >= batchEnd)
                            continue;

                        if (token.TokenType == TSqlTokenType.Variable &&
                            string.Equals(token.Text, variableName,
                                StringComparison.OrdinalIgnoreCase) &&
                            !execParamOffsets.Contains(token.Offset))
                        {
                            occurrences.Add(
                                (token.Offset, token.Text.Length));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //Infrastructure.OutputWindowLogger.LogError("FindAllVariableTokens parser error", ex);
            }

            return occurrences;
        }

        /// <summary>
        /// Scans the token stream after every EXEC[UTE] keyword and
        /// collects the offsets of parameter-name variables (the
        /// @name before '=' in <c>@name = @value</c> style calls).
        /// These belong to the stored-procedure signature, not to
        /// the local scope, so they are excluded from rename.
        /// </summary>
        private static HashSet<int> FindExecParameterNameOffsets(
            IList<TSqlParserToken> tokens)
        {
            var skipOffsets = new HashSet<int>();

            // T-SQL keywords that signal the end of an EXEC parameter list
            var statementKeywords = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
                "WITH",  "DECLARE", "SET",  "IF",  "ELSE",
                "WHILE", "BEGIN", "END", "RETURN", "GO",
                "CREATE", "ALTER", "DROP", "TRUNCATE"
            };

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                string text = token.Text?.ToUpperInvariant() ?? "";

                if (text != "EXEC" && text != "EXECUTE")
                    continue;

                // Scan forward from EXEC looking for @param = patterns
                bool inExecParams = true;
                for (int j = i + 1; j < tokens.Count && inExecParams; j++)
                {
                    var t = tokens[j];

                    if (t.TokenType == TSqlTokenType.WhiteSpace ||
                        t.TokenType == TSqlTokenType.SingleLineComment ||
                        t.TokenType == TSqlTokenType.MultilineComment ||
                        t.TokenType == TSqlTokenType.Dot ||
                        t.TokenType == TSqlTokenType.Comma ||
                        t.TokenType == TSqlTokenType.Semicolon)
                    {
                        continue;
                    }

                    string tText = t.Text?.ToUpperInvariant() ?? "";

                    // End of parameter list: new statement keyword
                    if (statementKeywords.Contains(tText))
                    {
                        inExecParams = false;
                        break;
                    }

                    // Found @variable — peek ahead for '='
                    if (t.TokenType == TSqlTokenType.Variable)
                    {
                        int peek = j + 1;
                        while (peek < tokens.Count &&
                               (tokens[peek].TokenType ==
                                    TSqlTokenType.WhiteSpace ||
                                tokens[peek].TokenType ==
                                    TSqlTokenType.SingleLineComment ||
                                tokens[peek].TokenType ==
                                    TSqlTokenType.MultilineComment))
                        {
                            peek++;
                        }

                        if (peek < tokens.Count &&
                            tokens[peek].TokenType ==
                                TSqlTokenType.EqualsSign)
                        {
                            skipOffsets.Add(t.Offset);
                        }
                    }
                }
            }

            return skipOffsets;
        }

        // ── Batch boundaries ────────────────────────────────────────────

        /// <summary>
        /// Finds the start and end offsets of the GO-delimited batch
        /// that contains the caret position.
        /// </summary>
        private static (int start, int end) FindBatchBoundaries(
            string text, int caretPos)
        {
            // Match GO as a standalone keyword (case-insensitive)
            var goMatches = Regex.Matches(text, @"\bGO\b",
                RegexOptions.IgnoreCase);

            int batchStart = 0;
            int batchEnd = text.Length;

            foreach (Match match in goMatches)
            {
                int goEnd = match.Index + match.Length;
                if (goEnd <= caretPos)
                {
                    batchStart = goEnd;
                }
                else if (match.Index > caretPos)
                {
                    batchEnd = match.Index;
                    break;
                }
            }

            // Skip leading whitespace / newlines after batch start
            while (batchStart < text.Length &&
                   char.IsWhiteSpace(text[batchStart]))
                batchStart++;

            return (batchStart, batchEnd);
        }

        /// <summary>
        /// Returns the index in the occurrences list that corresponds
        /// to the cursor position.
        /// </summary>
        private static int FindCursorOccurrenceIndex(
            List<(int offset, int length)> occurrences, int cursorOffset)
        {
            for (int i = 0; i < occurrences.Count; i++)
            {
                if (cursorOffset >= occurrences[i].offset &&
                    cursorOffset < occurrences[i].offset +
                                     occurrences[i].length)
                {
                    return i;
                }
            }

            // Fallback: closest occurrence before cursor
            for (int i = occurrences.Count - 1; i >= 0; i--)
            {
                if (occurrences[i].offset <= cursorOffset)
                    return i;
            }

            return 0;
        }

        // ── Active text view helper ─────────────────────────────────────

        private static IWpfTextView GetActiveWpfTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var textManager = Package.GetGlobalService(
                    typeof(SVsTextManager)) as IVsTextManager;
                if (textManager == null) return null;

                textManager.GetActiveView(1, null,
                    out IVsTextView activeView);
                if (activeView == null) return null;

                var componentModel = Package.GetGlobalService(
                    typeof(SComponentModel)) as IComponentModel;
                if (componentModel == null) return null;

                var adapterFactory = componentModel
                    .GetService<IVsEditorAdaptersFactoryService>();
                if (adapterFactory == null) return null;

                return adapterFactory.GetWpfTextView(activeView);
            }
            catch
            {
                return null;
            }
        }

        // ── Inline Rename Session ───────────────────────────────────────

        /// <summary>
        /// Manages the full lifecycle of an inline rename:
        /// enter rename mode → highlight + select →
        /// real-time sync on typing → commit / rollback.
        /// </summary>
        private sealed class InlineRenameSession : IDisposable
        {
            private readonly IWpfTextView _textView;
            private readonly string _originalName;
            private readonly OccurrenceRecord[] _records;
            private readonly int _cursorIndex;
            private readonly int _batchStart;
            private readonly int _batchEnd;
            private IAdornmentLayer _adornmentLayer;
            private bool _isUpdating;
            private bool _isDisposed;
            private string _originalFullText;

            private struct OccurrenceRecord
            {
                public int Offset;
                public int Length;
                public ITrackingSpan TrackingSpan;
            }

            public InlineRenameSession(IWpfTextView textView,
                string originalName,
                List<(int offset, int length)> occurrences,
                int cursorIndex,
                int batchStart,
                int batchEnd)
            {
                _textView = textView;
                _originalName = originalName;
                _cursorIndex = cursorIndex;
                _batchStart = batchStart;
                _batchEnd = batchEnd;

                // Build tracking spans for all occurrences
                var snapshot = textView.TextBuffer.CurrentSnapshot;
                _records = new OccurrenceRecord[occurrences.Count];

                for (int i = 0; i < occurrences.Count; i++)
                {
                    var span = new Span(
                        occurrences[i].offset,
                        occurrences[i].length);
                    _records[i] = new OccurrenceRecord
                    {
                        Offset = occurrences[i].offset,
                        Length = occurrences[i].length,
                        TrackingSpan = snapshot.CreateTrackingSpan(
                            span,
                            SpanTrackingMode.EdgeInclusive)
                    };
                }
            }

            public void BeginRename()
            {
                if (_isDisposed) return;

                // Store original text for rollback
                _originalFullText =
                    _textView.TextBuffer.CurrentSnapshot.GetText();

                // Highlight all non-cursor occurrences
                CreateHighlights();

                // Select only the name part after @ (or @@) so the
                // prefix stays visible and is preserved in sync
                var cursorRecord = _records[_cursorIndex];
                int prefixLen = _originalName.StartsWith("@@") ? 2 : 1;
                var selectSpan = new SnapshotSpan(
                    _textView.TextBuffer.CurrentSnapshot,
                    cursorRecord.Offset + prefixLen,
                    cursorRecord.Length - prefixLen);
                _textView.Selection.Select(selectSpan, false);
                _textView.Caret.MoveTo(selectSpan.End);

                // Listen for text changes (real-time sync)
                _textView.TextBuffer.Changed += OnTextBufferChanged;

                // Listen for Enter / Escape to commit or cancel
                _textView.VisualElement.PreviewKeyDown +=
                    OnPreviewKeyDown;

                // Encerrar a sessão quando o caret sai de TODAS as ocorrências
                // (clique/seta para fora = commit silencioso, como o rename inline
                // do VS). Sem isso a sessão ficava viva para sempre e passava a
                // reverter edições posteriores do usuário a cada TextBuffer.Changed.
                _textView.Caret.PositionChanged += OnCaretPositionChanged;

                _textView.Closed += OnTextViewClosed;
            }

            private void OnTextViewClosed(object sender, EventArgs e)
            {
                // Janela fechada durante a sessão: apenas soltar os eventos.
                _isDisposed = true;
                Unsubscribe();
            }

            /// <summary>
            /// Encerra silenciosamente a sessão quando o caret deixa todas as
            /// ocorrências — o texto digitado até aqui é mantido (commit implícito).
            /// </summary>
            private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
            {
                if (_isDisposed || _isUpdating) return;

                try
                {
                    var snapshot = _textView.TextBuffer.CurrentSnapshot;
                    int caretPos = e.NewPosition.BufferPosition.Position;

                    foreach (var rec in _records)
                    {
                        SnapshotSpan span = rec.TrackingSpan.GetSpan(snapshot);
                        if (caretPos >= span.Start.Position &&
                            caretPos <= span.End.Position)
                            return; // ainda dentro de uma ocorrência
                    }

                    // Saiu de todas: encerra sem rollback e sem diálogo.
                    _isDisposed = true;
                    Unsubscribe();
                    ClearHighlights();
                }
                catch
                {
                    // best-effort: nunca lançar no caminho do editor
                }
            }

            // ── Highlighting ─────────────────────────────────────────

            private void CreateHighlights()
            {
                try
                {
                    _adornmentLayer = _textView.GetAdornmentLayer(
                        "SQLsenseRenameHighlight");

                    // Remove any stale adornments from a previous session
                    _adornmentLayer.RemoveAllAdornments();

                    var snapshot = _textView.TextBuffer.CurrentSnapshot;

                    for (int i = 0; i < _records.Length; i++)
                    {
                        if (i == _cursorIndex) continue; // skip cursor

                        var span = _records[i].TrackingSpan
                            .GetSpan(snapshot);

                        // Skip zero-length (already replaced/deleted) spans
                        if (span.Length == 0) continue;

                        AddHighlightRect(span);
                    }
                }
                catch (Exception ex)
                {
                    //Infrastructure.OutputWindowLogger.LogError("InlineRename highlight creation failed", ex);
                }
            }

            private void AddHighlightRect(SnapshotSpan span)
            {
                var line = _textView.TextViewLines
                    .GetTextViewLineContainingBufferPosition(span.Start);
                if (line == null) return;

                var startBounds = line.GetCharacterBounds(span.Start);
                var endBounds = line.GetCharacterBounds(span.End);

                double width = endBounds.Left - startBounds.Left;
                double height = startBounds.TextHeight;

                var rect = new Rectangle
                {
                    Fill = new SolidColorBrush(
                        Color.FromArgb(48, 255, 140, 0)),
                    Width = Math.Max(width, 2),
                    Height = height
                };

                Canvas.SetLeft(rect, startBounds.Left);
                Canvas.SetTop(rect, startBounds.Top);

                _adornmentLayer.AddAdornment(
                    AdornmentPositioningBehavior.TextRelative,
                    span, null, rect, null);
            }

            private void ClearHighlights()
            {
                try
                {
                    _adornmentLayer?.RemoveAllAdornments();
                }
                catch { /* best-effort */ }
            }

            // ── Real-time sync ───────────────────────────────────────

            private void OnTextBufferChanged(object sender,
                TextContentChangedEventArgs e)
            {
                if (_isUpdating || _isDisposed) return;

                _isUpdating = true;
                try
                {
                    ThreadHelper.ThrowIfNotOnUIThread();

                    var snapshot = _textView.TextBuffer.CurrentSnapshot;

                    // IGNORAR edições fora da ocorrência do cursor: a sessão só
                    // sincroniza quando o usuário digita no identificador em edição.
                    // Sem este filtro, qualquer edição posterior em outro ponto do
                    // buffer disparava o sync — que sobrescrevia a edição do usuário
                    // e reconstruía todos os highlights a cada tecla (CPU alta).
                    var cursorTracking = _records[_cursorIndex].TrackingSpan;
                    bool touchesCursor = false;
                    foreach (var change in e.Changes)
                    {
                        SnapshotSpan cursorSpanNow = cursorTracking.GetSpan(snapshot);
                        int changeStart = Math.Min(change.NewPosition, snapshot.Length);
                        int changeEnd = Math.Min(change.NewEnd, snapshot.Length);

                        // interseção manual de intervalos [start, end]
                        if (changeStart <= cursorSpanNow.End.Position &&
                            changeEnd >= cursorSpanNow.Start.Position)
                        {
                            touchesCursor = true;
                            break;
                        }
                    }
                    if (!touchesCursor)
                    {
                        _isUpdating = false;
                        return;
                    }

                    // Read the current text at the cursor occurrence
                    var cursorSpan = cursorTracking.GetSpan(snapshot);
                    string newName = cursorSpan.GetText();

                    // If unchanged, skip
                    if (newName == _originalName &&
                        cursorSpan.Length == _records[_cursorIndex].Length)
                    {
                        _isUpdating = false;
                        return;
                    }

                    // Sync all other occurrences
                    using (var edit = _textView.TextBuffer.CreateEdit())
                    {
                        for (int i = 0; i < _records.Length; i++)
                        {
                            if (i == _cursorIndex) continue;

                            var span = _records[i].TrackingSpan
                                .GetSpan(snapshot);
                            string currentText = span.GetText();

                            if (currentText != newName)
                            {
                                edit.Replace(span.Start,
                                    span.Length, newName);
                            }
                        }

                        if (edit.HasEffectiveChanges)
                            edit.Apply();
                    }

                    // Re-draw highlights after sync
                    ClearHighlights();
                    CreateHighlights();
                }
                catch (Exception ex)
                {
                    //Infrastructure.OutputWindowLogger.LogError("InlineRename sync failed", ex);
                }
                finally
                {
                    _isUpdating = false;
                }
            }

            // ── Enter / Escape ───────────────────────────────────────

            private void OnPreviewKeyDown(object sender,
                System.Windows.Input.KeyEventArgs e)
            {
                if (_isDisposed) return;

                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    Commit();
                }
                else if (e.Key == System.Windows.Input.Key.Escape)
                {
                    e.Handled = true;
                    Rollback();
                }
            }

            private void Commit()
            {
                if (_isDisposed) return;

                // Read the new variable name from the cursor occurrence
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var cursorSpan = _records[_cursorIndex].TrackingSpan
                    .GetSpan(snapshot);
                string newName = cursorSpan.GetText();

                // If name didn't actually change, just cleanup
                if (string.Equals(newName, _originalName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    _isDisposed = true;
                    Unsubscribe();
                    ClearHighlights();
                    //Infrastructure.OutputWindowLogger.Log("InlineRename: name unchanged, cancelled.");
                    return;
                }

                // Prevent duplicate: scan batch for a pre-existing
                // variable with the same name
                if (HasDuplicateVariable(newName))
                {
                    System.Windows.MessageBox.Show(
                        $"Variable '{newName}' already exists in the " +
                        "current batch.",
                        "SQLsense — Duplicate Variable",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);

                    Rollback();
                    return;
                }

                _isDisposed = true;
                Unsubscribe();
                ClearHighlights();

                //Infrastructure.OutputWindowLogger.Log($"InlineRename: committed rename of " + $"'{_originalName}' → '{newName}'.");
            }

            /// <summary>
            /// Checks whether <paramref name="newName"/> already exists
            /// as a variable token elsewhere in the batch (i.e. outside
            /// the positions we are in the process of renaming).
            /// </summary>
            private bool HasDuplicateVariable(string newName)
            {
                try
                {
                    string fullText =
                        _textView.TextBuffer.CurrentSnapshot.GetText();

                    // Collect the buffer positions we already renamed
                    var ourPositions = new HashSet<int>();
                    var snapshot = _textView.TextBuffer.CurrentSnapshot;
                    foreach (var rec in _records)
                    {
                        var span = rec.TrackingSpan.GetSpan(snapshot);
                        ourPositions.Add(span.Start.Position);
                    }

                    var parser = new TSql160Parser(true);
                    using (var reader = new StringReader(fullText))
                    {
                        IList<ParseError> errors;
                        var fragment = parser.Parse(reader, out errors);

                        if (fragment?.ScriptTokenStream == null)
                            return false;

                        foreach (var token in fragment.ScriptTokenStream)
                        {
                            if (token.Offset < _batchStart ||
                                token.Offset >= _batchEnd)
                                continue;

                            if (token.TokenType != TSqlTokenType.Variable)
                                continue;

                            if (!string.Equals(token.Text, newName,
                                StringComparison.OrdinalIgnoreCase))
                                continue;

                            // Skip positions we just renamed
                            if (ourPositions.Contains(token.Offset))
                                continue;

                            // Found a pre-existing variable with same name
                            return true;
                        }
                    }
                }
                catch
                {
                    // If duplicate check fails, allow the rename
                    // (fail-open is safer than blocking valid renames)
                }

                return false;
            }

            private void Rollback()
            {
                if (_isDisposed) return;
                _isDisposed = true;

                Unsubscribe();
                ClearHighlights();

                // Restore original text
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    using (var edit = _textView.TextBuffer.CreateEdit())
                    {
                        int currentLength =
                            _textView.TextBuffer.CurrentSnapshot.Length;
                        edit.Replace(0, currentLength, _originalFullText);
                        edit.Apply();
                    }

                    //Infrastructure.OutputWindowLogger.Log("InlineRename: rolled back.");
                }
                catch (Exception ex)
                {
                    //Infrastructure.OutputWindowLogger.LogError("InlineRename rollback failed", ex);
                }
            }

            private void Unsubscribe()
            {
                _textView.TextBuffer.Changed -= OnTextBufferChanged;
                _textView.VisualElement.PreviewKeyDown -=
                    OnPreviewKeyDown;
                _textView.Caret.PositionChanged -= OnCaretPositionChanged;
                _textView.Closed -= OnTextViewClosed;
            }

            public void Dispose()
            {
                if (!_isDisposed)
                    Rollback();
            }
        }
    }
}
