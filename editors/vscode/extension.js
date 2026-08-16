const path = require('node:path');
const vscode = require('vscode');
const {
    LanguageClient,
    TransportKind,
} = require('vscode-languageclient/node');

let client;

/** @param {vscode.ExtensionContext} context */
async function activate(context) {
    const configuration = vscode.workspace.getConfiguration('lucent');
    const command = configuration.get('languageServer.command', 'dotnet');
    const configuredPath = configuration.get(
        'languageServer.path',
        'server/Lucent.LanguageServer.dll');
    const trace = configuration.get('languageServer.trace', 'messages');
    const serverPath = path.isAbsolute(configuredPath)
        ? configuredPath
        : path.join(context.extensionPath, configuredPath);

    const serverOptions = {
        run: {
            command,
            args: command === 'dotnet' ? [serverPath] : [],
            transport: TransportKind.stdio,
            options: {
                env: {
                    ...process.env,
                    LUCENT_LANGUAGE_SERVER_TRACE: trace,
                },
            },
        },
        debug: {
            command,
            args: command === 'dotnet' ? [serverPath] : [],
            transport: TransportKind.stdio,
            options: {
                env: {
                    ...process.env,
                    LUCENT_LANGUAGE_SERVER_TRACE: 'verbose',
                },
            },
        },
    };

    const clientOptions = {
        documentSelector: [
            { scheme: 'file', language: 'lucent' },
        ],
        diagnosticCollectionName: 'lucent',
        outputChannelName: 'Lucent Language Server',
        synchronize: {
            configurationSection: 'lucent',
        },
    };

    client = new LanguageClient(
        'lucentLanguageServer',
        'Lucent Language Server',
        serverOptions,
        clientOptions);
    context.subscriptions.push(vscode.commands.registerCommand(
        'lucent.showLanguageServerOutput',
        () => client?.outputChannel.show()));
    await client.start();
    context.subscriptions.push({
        dispose: () => {
            void client?.stop();
        },
    });
}

function deactivate() {
    return client?.stop();
}

module.exports = {
    activate,
    deactivate,
};
