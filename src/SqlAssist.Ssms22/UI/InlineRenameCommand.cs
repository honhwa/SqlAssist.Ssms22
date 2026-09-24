using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Ssms22.Editor;
using SqlAssist.Ssms22.Settings;
using Task = System.Threading.Tasks.Task;

namespace SqlAssist.Ssms22.UI
{
    /// <summary>
    /// SQL Prompt–style inline variable rename.
    ///
    /// F2 with the caret on a @variable:
    ///   • All occurrences in the current GO batch are selected and renamed
    ///     in real time via <see cref="SqlVariableRenameSession"/>.
    ///   • Enter commits, Escape rolls back the whole rename.
    /// </summary>
    internal sealed class InlineRenameCommand
    {
        // Command ID must match cmdidRenameVariable in VSCommandTable.vsct
        // (0x021C). The VSCT-declared ID is the single source of truth: the
        // menu button, toolbar placement and F2 key binding all reference it.
        public const int CommandId = 0x21C;
        public static readonly Guid CommandSet = new Guid("4a188946-9364-4f07-af7e-97f3bd7ca7a7");

        private readonly AsyncPackage _package;

        private InlineRenameCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package
                ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService
                ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(this.Execute, menuCommandID);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static InlineRenameCommand? Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync(package.DisposalToken);

            if (await package.GetServiceAsync(typeof(IMenuCommandService))
                is not OleMenuCommandService commandService)
            {
                SqlAssistDiagnostics.WriteAlways(
                    "InlineRenameCommand: 無法取得 OleMenuCommandService，命令未註冊。");
                return;
            }

            Instance = new InlineRenameCommand(package, commandService);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand command)
            {
                command.Enabled = SqlAssistSettingsStore.Current.Enabled
                    && ActiveSqlEditor.Current is not null;
                command.Visible = true;
            }
        }

        // ── Execute ─────────────────────────────────────────────────────

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var textView = ActiveSqlEditor.Current;
                if (textView == null)
                {
                    SqlAssistStatusBar.Show(_package, "請先把游標放進 SQL 查詢視窗。");
                    return;
                }

                if (!SqlVariableRenameSession.Begin(textView, _package, out var message))
                {
                    if (!string.IsNullOrEmpty(message))
                    {
                        SqlAssistStatusBar.Show(_package, message);
                    }
                }
            }
            catch (Exception ex)
            {
                SqlAssistDiagnostics.WriteAlways($"InlineRename failed: {ex}");
                SqlAssistStatusBar.Show(_package, $"重新命名變數失敗：{ex.Message}");
            }
        }

    }
}
