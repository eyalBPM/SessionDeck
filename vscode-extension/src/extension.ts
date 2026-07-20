// SessionDeck Connector (SPEC stage D).
//
// Outbound: keeps a persistent connection to SessionDeck's named pipe and pushes a
// "vscode-sync" snapshot (workspace folder, git branch, open Claude Code tabs) on
// activation and on every tab/branch change.
//
// Inbound: SessionDeck pushes commands down the same connection. "openSession"
// delegates to Claude Code's own claude-vscode.editor.open, which reveals the tab
// if the session is open and resumes it if not — the extension holds the
// session_id↔tab map, so no correlation is needed on our side.

import * as vscode from 'vscode';
import * as net from 'net';

const PIPE_PATH = '\\\\.\\pipe\\sessiondeck';
const RECONNECT_MS = 5000;
const SYNC_DEBOUNCE_MS = 300;
const HEARTBEAT_MS = 2000;         // must stay well under SessionDeck's ActiveTabTtl
const CLAUDE_VIEWTYPE = 'claudeVSCodePanel';   // actual viewType is prefixed (mainThreadWebview-...)

let out: vscode.OutputChannel;
let socket: net.Socket | undefined;
let connected = false;
let reconnectTimer: NodeJS.Timeout | undefined;
let syncTimer: NodeJS.Timeout | undefined;
let gitApi: any;
const hookedRepos = new WeakSet<object>();

function workspacePath(): string {
    return vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? '';
}

function claudeTabs(): { Label: string; Active: boolean }[] {
    const tabs: { Label: string; Active: boolean }[] = [];
    // isActive is per-group: with split editor groups EVERY group has an active tab, and
    // SessionDeck auto-acknowledges the first Active it sees. Only the focused group's
    // active tab is what the user is actually looking at (issue 2026-07-20).
    const activeGroup = vscode.window.tabGroups.activeTabGroup;
    for (const group of vscode.window.tabGroups.all) {
        for (const tab of group.tabs) {
            const input = tab.input;
            if (input instanceof vscode.TabInputWebview && input.viewType.includes(CLAUDE_VIEWTYPE)) {
                tabs.push({ Label: tab.label, Active: tab.isActive && group === activeGroup });
            }
        }
    }
    return tabs;
}

function currentBranch(): string {
    try {
        const head = gitApi?.repositories?.[0]?.state?.HEAD;
        return head?.name ?? (head?.commit ? head.commit.slice(0, 7) : '');
    } catch {
        return '';
    }
}

function sendSync(): void {
    if (!connected || !socket) {
        return;
    }
    const msg = {
        Type: 'vscode-sync',
        Workspace: workspacePath(),
        Branch: currentBranch(),
        Pid: process.pid,
        Focused: vscode.window.state.focused,
        Tabs: claudeTabs(),
    };
    try {
        socket.write(JSON.stringify(msg) + '\n');
    } catch (e) {
        out.appendLine(`send failed: ${e}`);
    }
}

function queueSync(): void {
    if (syncTimer) {
        clearTimeout(syncTimer);
    }
    syncTimer = setTimeout(sendSync, SYNC_DEBOUNCE_MS);
}

/// The event-driven syncs above are the only thing telling SessionDeck which tab the user
/// is looking at, and that answer is what suppresses a session's blink. One dropped sync
/// (pipe down, reconnect window, a second VSCode window on the same workspace racing us)
/// therefore leaves the deck acting on a stale answer forever — it silences a blink the
/// user never saw. A heartbeat gives the deck something to age out against.
///
/// Only while focused: an unfocused window suppresses nothing, so its state is not worth
/// a packet (issue 2026-07-20).
function startHeartbeat(context: vscode.ExtensionContext): void {
    const timer = setInterval(() => {
        if (vscode.window.state.focused) {
            sendSync();
        }
    }, HEARTBEAT_MS);
    context.subscriptions.push({ dispose: () => clearInterval(timer) });
}

async function handleCommand(raw: string): Promise<void> {
    let cmd: any;
    try {
        cmd = JSON.parse(raw);
    } catch {
        out.appendLine(`bad command line: ${raw}`);
        return;
    }
    const name = cmd.Cmd ?? cmd.cmd;
    if (name === 'openSession') {
        const sessionId = cmd.SessionId ?? cmd.sessionId;
        if (sessionId) {
            await openClaudePanel(sessionId, cmd.Maximize ?? cmd.maximize);
        }
    } else if (name === 'newSession') {
        // claude-vscode.editor.open without a session id opens a fresh conversation tab.
        await openClaudePanel(undefined, cmd.Maximize ?? cmd.maximize);
    } else {
        out.appendLine(`unknown command: ${name}`);
    }
}

async function openClaudePanel(sessionId: string | undefined, maximize: boolean): Promise<void> {
    out.appendLine(`${sessionId ? `openSession ${sessionId}` : 'newSession'} (maximize=${maximize})`);
    if (maximize) {
        // "Full tab area": collapse both side bars and the bottom panel first.
        for (const c of ['workbench.action.closeSidebar', 'workbench.action.closePanel', 'workbench.action.closeAuxiliaryBar']) {
            try {
                await vscode.commands.executeCommand(c);
            } catch { /* layout command unavailable — ignore */ }
        }
    }
    try {
        // Claude Code's reveal-or-resume (or new conversation when no id). ViewColumn.Active
        // keeps it in the current editor group and doesn't touch the location preference.
        await vscode.commands.executeCommand('claude-vscode.editor.open', sessionId, undefined, vscode.ViewColumn.Active);
    } catch (e) {
        // Internal command signature changed / Claude extension missing — guaranteed fallback.
        out.appendLine(`claude-vscode.editor.open failed (${e}) — falling back to terminal`);
        try {
            const term = vscode.window.createTerminal({ name: 'Claude Code' });
            term.show();
            term.sendText(sessionId ? `claude --resume ${sessionId}` : 'claude');
            void vscode.window.showWarningMessage(
                'SessionDeck: פתיחת הסשן דרך Claude Code נכשלה — ייתכן שה-API הפנימי השתנה בעדכון. ' +
                'בוצע fallback לטרמינל. פרטים: Output ← SessionDeck.');
        } catch (e2) {
            out.appendLine(`terminal fallback failed too: ${e2}`);
            void vscode.window.showErrorMessage(
                'SessionDeck: פתיחת הסשן נכשלה לחלוטין (גם ה-fallback לטרמינל). פרטים: Output ← SessionDeck.');
        }
    }
}

function connect(): void {
    const s = net.connect(PIPE_PATH);
    socket = s;
    let buffer = '';

    s.on('connect', () => {
        connected = true;
        out.appendLine('connected to SessionDeck');
        sendSync();
    });
    s.on('data', (chunk) => {
        buffer += chunk.toString('utf8');
        let idx;
        while ((idx = buffer.indexOf('\n')) >= 0) {
            const line = buffer.slice(0, idx).trim();
            buffer = buffer.slice(idx + 1);
            if (line) {
                void handleCommand(line);
            }
        }
    });
    const retry = () => {
        if (socket !== s) {
            return;                      // stale socket ('close' after 'error', or replaced)
        }
        if (connected) {
            out.appendLine('disconnected from SessionDeck — retrying');
        }
        connected = false;
        socket = undefined;
        if (reconnectTimer) {
            clearTimeout(reconnectTimer);
        }
        reconnectTimer = setTimeout(connect, RECONNECT_MS);
    };
    s.on('error', retry);
    s.on('close', retry);
}

async function initGit(context: vscode.ExtensionContext): Promise<void> {
    try {
        const ext = vscode.extensions.getExtension('vscode.git');
        if (!ext) {
            return;
        }
        const exports = ext.isActive ? ext.exports : await ext.activate();
        gitApi = exports.getAPI(1);
        const hook = (repo: any) => {
            if (hookedRepos.has(repo)) {
                return;
            }
            hookedRepos.add(repo);
            context.subscriptions.push(repo.state.onDidChange(queueSync));
        };
        gitApi.repositories.forEach(hook);
        context.subscriptions.push(gitApi.onDidOpenRepository((repo: any) => {
            hook(repo);
            queueSync();
        }));
        queueSync();                     // branch is known now
    } catch (e) {
        out.appendLine(`git API unavailable: ${e}`);
    }
}

export function activate(context: vscode.ExtensionContext): void {
    out = vscode.window.createOutputChannel('SessionDeck');
    context.subscriptions.push(out);
    out.appendLine(`SessionDeck Connector activated for: ${workspacePath() || '(no folder)'}`);

    context.subscriptions.push(vscode.window.tabGroups.onDidChangeTabs(queueSync));
    context.subscriptions.push(vscode.window.tabGroups.onDidChangeTabGroups(queueSync));
    context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders(queueSync));
    context.subscriptions.push(vscode.window.onDidChangeWindowState(queueSync));
    context.subscriptions.push(vscode.commands.registerCommand('sessiondeck.sync', () => {
        out.appendLine('manual sync');
        sendSync();
    }));

    startHeartbeat(context);
    void initGit(context);
    connect();
}

export function deactivate(): void {
    if (reconnectTimer) {
        clearTimeout(reconnectTimer);
    }
    if (syncTimer) {
        clearTimeout(syncTimer);
    }
    socket?.destroy();
    socket = undefined;
}
